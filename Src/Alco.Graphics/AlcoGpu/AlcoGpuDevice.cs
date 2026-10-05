using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics.AlcoGpu.Interop;

namespace Alco.Graphics.AlcoGpu;

internal sealed unsafe partial class AlcoGpuDevice : GPUDevice
{
    #region Properties

    private const int MaxPendingTextureReadbacks = 16;

    // Staging-buffer cache constants. See Docs/Spec/2026-06-13-gpu-readback-staging-buffer-cache-design.md.
    private const ulong StagingCacheIdleBudget = 64UL * 1024 * 1024;
    private const ulong StagingCacheSingleBufferMax = 64UL * 1024 * 1024;
    private static readonly long _stagingCacheIdleExpirationTicks = (long)(10.0 * Stopwatch.Frequency); // 10 seconds
    private const ulong StagingCacheOversizeReuseThreshold = 4UL * 1024 * 1024;

    private readonly DeviceDescriptor _descriptor;
    // Serializes texture uploads with every submission (including readbacks).
    // wgpu-core historically took texture-initialization and device-tracker locks
    // in opposite orders in write_texture and submit; this lock prevents a
    // first-use deadlock. Buffer writes hold no initialization lock and need no
    // extra synchronization.
    private readonly Lock _textureUploadLock = new();
    private readonly List<PendingTextureReadback> _pendingTextureReadbacks = new(capacity: 4);

    // Native staging-buffer cache. The policy holds no native handles; the ticket
    // carries the alco buffer handle plus its capacity. All list access is under
    // _stagingCacheLock; native calls (create/destroy) are made outside the lock.
    private readonly struct StagingTicket
    {
        public readonly AlcoHandle Handle;
        public readonly ulong Capacity;

        public StagingTicket(AlcoHandle handle, ulong capacity)
        {
            Handle = handle;
            Capacity = capacity;
        }
    }

    private readonly ReadbackStagingBufferCachePolicy<StagingTicket> _stagingCache =
        new(StagingCacheIdleBudget, StagingCacheSingleBufferMax, _stagingCacheIdleExpirationTicks, StagingCacheOversizeReuseThreshold);
    private readonly Lock _stagingCacheLock = new();
    private readonly List<StagingTicket> _stagingCacheEvicted = new(capacity: 4);

    private readonly PixelFormat _preferredSurfaceFormat;
    private readonly int _maxBindGroups;

    internal AlcoHandle Native { get; }

    public bool IsDebug { get; }

    /// <summary>
    /// Whether the device exposes passthrough shaders: slang's SPIR-V (Vulkan),
    /// DXIL (D3D12) and MSL/metallib (Metal) reach the backend as-is. Required
    /// for DXIL/MSL/MetalLib (no translation fallback exists); SPIR-V falls
    /// back to Naga import natively when unavailable.
    /// </summary>
    internal bool ShaderPassthroughEnabled { get; }

    /// <summary>The backend the adapter actually selected (Auto resolves per platform).</summary>
    public override GraphicsBackend Backend { get; }

    #endregion

    private struct PendingTextureReadback
    {
        public GPUTextureReadbackRequest Request;
        public StagingTicket Buffer;
        public ulong StagingDataSize;
        public uint DataSize;
        public uint TightBytesPerRow;
        public uint AlignedBytesPerRow;
        public uint Height;
        public uint Depth;
        public byte* Destination;
    }

    private struct TextureReadbackLayout
    {
        public AlcoCopyLayout BufferLayout;
        public AlcoExtent3D CopySize;
        public ulong StagingDataSize;
        public uint DataSize;
        public uint TightBytesPerRow;
        public uint AlignedBytesPerRow;
        public uint Height;
        public uint Depth;
    }

    #region Abstract Implementation

    public override PixelFormat PreferredSurfaceFormat
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _preferredSurfaceFormat;
    }

    /// <summary>
    /// The default bind groups shared across the entire device.
    /// </summary>
    public override GPUBindGroup BindGroupUniformBuffer { get; }
    public override GPUBindGroup BindGroupStorageBuffer { get; }
    public override GPUBindGroup BindGroupStorageBufferWithCounter { get; }
    public override GPUBindGroup BindGroupTexture2DRead { get; }
    public override GPUBindGroup BindGroupTexture2DStorage { get; }
    public override GPUBindGroup BindGroupTexture3DRead { get; }

    public override GPUFeatures SupportedFeatures { get; }

    public override float TimestampPeriodNanoseconds { get; }

    /// <summary>
    /// The maximum number of bind groups supported by the adapter.
    /// </summary>
    public override int MaxBindGroups
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _maxBindGroups;
    }

    protected override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
        AlcoHandle buffer = ((AlcoGpuCommandBuffer)commandBuffer).TakeBuffer();
        lock (_textureUploadLock)
        {
            // The native side consumes the command-buffer handle on submit.
            uint status = AlcoGpuNative.QueueSubmit(Native, buffer, null);
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    protected unsafe override void DisposeCore()
    {
        FailPendingTextureReadbacks(
            new ObjectDisposedException(nameof(AlcoGpuDevice), "GPU device was disposed before texture readback completed."));

        // Release all idle cached staging buffers. Pending buffers were destroyed above by
        // FailPendingTextureReadbacks; idle buffers are drained here and destroyed natively.
        lock (_stagingCacheLock)
        {
            _stagingCache.Drain(_stagingCacheEvicted);
        }
        DestroyEvicted();

        // Dispose default resources
        BindGroupUniformBuffer.Destroy();
        BindGroupStorageBuffer.Destroy();
        BindGroupStorageBufferWithCounter.Destroy();
        BindGroupTexture2DRead.Destroy();
        BindGroupTexture2DStorage.Destroy();

        // Destroys the native device context including its wgpu Global; every
        // remaining object handle becomes invalid (double destroy is detected).
        uint status = AlcoGpuNative.DeviceDestroy(Native);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor)
    {
        return new AlcoGpuBuffer(this, descriptor);
    }

    protected override GPUTimestampQuerySet CreateTimestampQuerySetCore(uint count, string name)
    {
        return new AlcoGpuTimestampQuerySet(this, count, name);
    }

    protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null)
    {
        return new AlcoGpuCommandBuffer(this, descriptor);
    }

    protected override GPURenderBundle CreateRenderBundleCore(in RenderBundleDescriptor? descriptor)
    {
        return new AlcoGpuRenderBundle(this, descriptor);
    }

    protected override GPUTexture CreateTextureCore(in TextureDescriptor descriptor)
    {
        return new AlcoGpuTexture(this, descriptor);
    }

    protected override GPUAttachmentLayout CreateAttachmentLayoutCore(in AttachmentLayoutDescriptor descriptor)
    {
        return new AlcoGpuAttachmentLayout(this, descriptor);
    }

    protected override GPUFrameBuffer CreateFrameBufferCore(in FrameBufferDescriptor descriptor)
    {
        return new AlcoGpuFrameBuffer(this, descriptor);
    }

    protected override GPUFrameBuffer CreateExternalFrameBufferCore(in ExternalFrameBufferDescriptor descriptor)
    {
        return new AlcoGpuExternalFrameBuffer(this, descriptor);
    }

    protected override GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor)
    {
        return new AlcoGpuGraphicsPipeline(this, descriptor);
    }

    protected override GPUPipeline CreateComputePipelineCore(in ComputePipelineDescriptor descriptor)
    {
        return new AlcoGpuComputePipeline(this, descriptor);
    }

    protected override GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor)
    {
        return new AlcoGpuBindGroup(this, descriptor);
    }

    protected override GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor)
    {
        return new AlcoGpuResourceGroup(this, descriptor);
    }

    protected override GPUTextureView CreateTextureViewCore(in TextureViewDescriptor descriptor)
    {
        return new AlcoGpuTextureView(this, descriptor);
    }

    protected override GPUSampler CreateSamplerCore(in SamplerDescriptor descriptor)
    {
        return new AlcoGpuSampler(this, descriptor);
    }

    public override GPUSwapchain CreateSwapchainCore(in SwapchainDescriptor descriptor)
    {
        return new AlcoGpuSwapchain(this, descriptor);
    }

    protected override unsafe void WriteBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        AlcoHandle nativeBuffer = ((AlcoGpuBuffer)buffer).Native;
        uint status = AlcoGpuNative.QueueWriteBuffer(Native, nativeBuffer, bufferOffset, data, size);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    protected override unsafe void ReadBufferCore(GPUBuffer buffer, byte* dest, uint bufferOffset, uint size)
    {
        AlcoHandle nativeBuffer = ((AlcoGpuBuffer)buffer).Native;
        StagingTicket tmpBuffer = AcquireStagingBuffer(size);
        bool succeeded = false;
        bool wasMapped = false;
        try
        {
            ulong submissionIndex = CopyBufferToStaging(nativeBuffer, bufferOffset, tmpBuffer.Handle, size);

            AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferMapRead(Native, tmpBuffer.Handle, 0, size));
            PollAndWait(submissionIndex);
            AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferMapPoll(Native, tmpBuffer.Handle));
            wasMapped = true;

            void* pointer = GetMappedRange(tmpBuffer.Handle, size);
            Unsafe.CopyBlock(dest, pointer, size);

            succeeded = true;
        }
        finally
        {
            if (wasMapped)
            {
                AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferUnmap(Native, tmpBuffer.Handle));
            }

            if (succeeded)
            {
                ReturnStagingBuffer(tmpBuffer);
            }
            else
            {
                ReleaseReadbackBuffer(tmpBuffer);
            }
        }
    }

    protected override unsafe void WriteTextureCore(GPUTexture texture, byte* data, uint dataSize, uint mipLevel)
    {
        AlcoHandle nativeTexture = ((AlcoGpuTextureBase)texture).Native;

        // The write covers exactly the given mip level's extent, not the level-0 size.
        uint mipWidth = Math.Max(1u, texture.Width >> (int)mipLevel);
        uint mipHeight = Math.Max(1u, texture.Height >> (int)mipLevel);

        AlcoCopyLayout layout = AlcoGpuUtility.GetTextureDataLayout(texture.PixelFormat, mipWidth, mipHeight);
        AlcoExtent3D writeSize = new()
        {
            Width = mipWidth,
            Height = mipHeight,
            DepthOrArrayLayers = texture.Depth,
        };
        AlcoOrigin3D origin = default;

        lock (_textureUploadLock)
        {
            uint status = AlcoGpuNative.QueueWriteTexture(
                Native, nativeTexture, mipLevel, origin, (uint)TextureAspect.All, data, dataSize, in layout, writeSize);
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    protected override unsafe void WriteTextureRegionCore(
        GPUTexture texture,
        byte* data,
        uint dataSize,
        uint bytesPerRow,
        uint x,
        uint y,
        uint width,
        uint height,
        uint mipLevel)
    {
        AlcoHandle nativeTexture = ((AlcoGpuTextureBase)texture).Native;

        // The source layout is caller-controlled: rows are packed at bytesPerRow
        // (256-byte aligned for multi-row regions), so the copy reads exactly the
        // rectangle's rows.
        AlcoCopyLayout layout = new()
        {
            Offset = 0,
            BytesPerRow = bytesPerRow,
            RowsPerImage = height,
        };

        AlcoOrigin3D origin = new() { X = x, Y = y, Z = 0 };
        AlcoExtent3D writeSize = new()
        {
            Width = width,
            Height = height,
            DepthOrArrayLayers = 1,
        };

        lock (_textureUploadLock)
        {
            uint status = AlcoGpuNative.QueueWriteTexture(
                Native, nativeTexture, mipLevel, origin, (uint)TextureAspect.All, data, dataSize, in layout, writeSize);
            AlcoGpuMarshal.ThrowIfFailed(status);
        }
    }

    protected override unsafe void ReadTextureCore(GPUTexture texture, byte* dest, uint dataSize, uint mipLevel = 0)
    {
        // AlcoGpuTextureBase, not AlcoGpuTexture: swapchain surface textures are readable too.
        AlcoHandle nativeTexture = ((AlcoGpuTextureBase)texture).Native;
        TextureReadbackLayout layout = GetTextureReadbackLayout(texture, dataSize, mipLevel);

        StagingTicket tmpBuffer = default;
        bool acquired = false;
        bool wasMapped = false;

        try
        {
            tmpBuffer = AcquireStagingBuffer(layout.StagingDataSize);
            acquired = true;

            ulong submissionIndex = CopyTextureToStaging(
                nativeTexture, mipLevel, tmpBuffer.Handle, in layout.BufferLayout, layout.CopySize);

            AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferMapRead(Native, tmpBuffer.Handle, 0, layout.StagingDataSize));
            PollAndWait(submissionIndex);
            AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferMapPoll(Native, tmpBuffer.Handle));
            wasMapped = true;

            void* pointer = GetMappedRange(tmpBuffer.Handle, layout.StagingDataSize);
            CopyCompletedTextureReadback(dest, pointer, layout);
        }
        catch
        {
            // On any failure the buffer must not be returned to the cache; unmap (if
            // needed) and destroy it instead.
            if (wasMapped && acquired)
            {
                AlcoGpuNative.BufferUnmap(Native, tmpBuffer.Handle);
                wasMapped = false;
            }

            if (acquired)
            {
                ReleaseReadbackBuffer(tmpBuffer);
                acquired = false;
            }

            throw;
        }
        finally
        {
            if (wasMapped)
            {
                AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferUnmap(Native, tmpBuffer.Handle));
            }

            if (acquired)
            {
                ReturnStagingBuffer(tmpBuffer);
            }
        }
    }

    protected override unsafe void BeginReadTextureCore(
        GPUTexture texture,
        byte* dest,
        uint dataSize,
        GPUTextureReadbackRequest request,
        uint mipLevel = 0)
    {
        AlcoHandle nativeTexture = ((AlcoGpuTextureBase)texture).Native;
        TextureReadbackLayout layout = GetTextureReadbackLayout(texture, dataSize, mipLevel);

        StagingTicket tmpBuffer = AcquireStagingBuffer(layout.StagingDataSize);
        try
        {
            CopyTextureToStaging(nativeTexture, mipLevel, tmpBuffer.Handle, in layout.BufferLayout, layout.CopySize);

            // The map completes when the submission finishes; polled each frame
            // from ProcessPendingReadbacksCore.
            AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.BufferMapRead(Native, tmpBuffer.Handle, 0, layout.StagingDataSize));

            _pendingTextureReadbacks.Add(new PendingTextureReadback
            {
                Request = request,
                Buffer = tmpBuffer,
                StagingDataSize = layout.StagingDataSize,
                DataSize = layout.DataSize,
                TightBytesPerRow = layout.TightBytesPerRow,
                AlignedBytesPerRow = layout.AlignedBytesPerRow,
                Height = layout.Height,
                Depth = layout.Depth,
                Destination = dest,
            });
            tmpBuffer = default;
        }
        finally
        {
            if (!tmpBuffer.Handle.IsNull)
            {
                ReleaseReadbackBuffer(tmpBuffer);
            }
        }
    }

    private static TextureReadbackLayout GetTextureReadbackLayout(GPUTexture texture, uint dataSize, uint mipLevel)
    {
        if (mipLevel >= texture.MipLevelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mipLevel),
                mipLevel,
                $"Texture only has {texture.MipLevelCount} mip levels.");
        }

        uint width = texture.GetMipWidth(mipLevel);
        uint height = texture.GetMipHeight(mipLevel);
        uint depth = texture.GetMipDepth(mipLevel);

        AlcoCopyLayout bufferLayout;
        ulong stagingDataSize = dataSize;
        uint tightBytesPerRow = 0;
        uint alignedBytesPerRow = 0;

        if (PixelFormatUtility.TryGetPixelSize(texture.PixelFormat, out uint pixelSize))
        {
            tightBytesPerRow = checked(width * pixelSize);
            alignedBytesPerRow = AlignTextureBytesPerRow(tightBytesPerRow);
            ulong expectedDataSize = (ulong)tightBytesPerRow * height * depth;
            if (dataSize < expectedDataSize)
            {
                throw new GraphicsException(
                    $"The destination buffer is too small for texture readback. Required: {expectedDataSize}, provided: {dataSize}.");
            }

            bufferLayout = new AlcoCopyLayout
            {
                Offset = 0,
                BytesPerRow = alignedBytesPerRow,
                RowsPerImage = height,
            };
            stagingDataSize = (ulong)alignedBytesPerRow * height * depth;
        }
        else
        {
            bufferLayout = AlcoGpuUtility.GetTextureDataLayout(texture.PixelFormat, width, height);
        }

        return new TextureReadbackLayout
        {
            BufferLayout = bufferLayout,
            CopySize = new AlcoExtent3D
            {
                Width = width,
                Height = height,
                DepthOrArrayLayers = depth,
            },
            StagingDataSize = stagingDataSize,
            DataSize = dataSize,
            TightBytesPerRow = tightBytesPerRow,
            AlignedBytesPerRow = alignedBytesPerRow,
            Height = height,
            Depth = depth,
        };
    }

    private static uint AlignTextureBytesPerRow(uint bytesPerRow)
    {
        const uint TextureCopyBytesPerRowAlignment = 256;
        return checked((bytesPerRow + TextureCopyBytesPerRowAlignment - 1) & ~(TextureCopyBytesPerRowAlignment - 1));
    }

    private static unsafe void CopyTextureReadbackData(
        byte* destination,
        byte* source,
        uint tightBytesPerRow,
        uint alignedBytesPerRow,
        uint height,
        uint depth)
    {
        if (tightBytesPerRow == alignedBytesPerRow)
        {
            Unsafe.CopyBlock(destination, source, checked(tightBytesPerRow * height * depth));
            return;
        }

        for (uint layer = 0; layer < depth; layer++)
        {
            ulong sourceLayerOffset = (ulong)layer * height * alignedBytesPerRow;
            ulong destinationLayerOffset = (ulong)layer * height * tightBytesPerRow;

            for (uint row = 0; row < height; row++)
            {
                byte* sourceRow = source + (nint)(sourceLayerOffset + (ulong)row * alignedBytesPerRow);
                byte* destinationRow = destination + (nint)(destinationLayerOffset + (ulong)row * tightBytesPerRow);
                Unsafe.CopyBlock(destinationRow, sourceRow, tightBytesPerRow);
            }
        }
    }

    private static unsafe void CopyCompletedTextureReadback(byte* destination, void* mappedPointer, in TextureReadbackLayout layout)
    {
        if (mappedPointer == null)
        {
            throw new GraphicsException("Texture readback returned a null mapped range.");
        }

        if (layout.TightBytesPerRow != 0)
        {
            CopyTextureReadbackData(
                destination,
                (byte*)mappedPointer,
                layout.TightBytesPerRow,
                layout.AlignedBytesPerRow,
                layout.Height,
                layout.Depth);
            return;
        }

        Unsafe.CopyBlock(destination, mappedPointer, layout.DataSize);
    }

    private static unsafe void CopyCompletedTextureReadback(byte* destination, void* mappedPointer, in PendingTextureReadback readback)
    {
        if (mappedPointer == null)
        {
            throw new GraphicsException("Texture readback returned a null mapped range.");
        }

        if (readback.TightBytesPerRow != 0)
        {
            CopyTextureReadbackData(
                destination,
                (byte*)mappedPointer,
                readback.TightBytesPerRow,
                readback.AlignedBytesPerRow,
                readback.Height,
                readback.Depth);
            return;
        }

        Unsafe.CopyBlock(destination, mappedPointer, readback.DataSize);
    }

    private unsafe void ProcessPendingTextureReadbacks()
    {
        // Pump the native queue without blocking so in-flight maps can complete.
        if (_pendingTextureReadbacks.Count > 0)
        {
            AlcoGpuNative.DevicePoll(Native, AlcoGpuAbi.AlcoFalse, ulong.MaxValue, null);
        }

        for (int i = 0; i < _pendingTextureReadbacks.Count; i++)
        {
            PendingTextureReadback readback = _pendingTextureReadbacks[i];
            uint pollStatus = AlcoGpuNative.BufferMapPoll(Native, readback.Buffer.Handle);
            if (pollStatus == AlcoGpuAbi.Status.NotReady)
            {
                continue;
            }

            bool succeeded = false;
            bool wasMapped = false;
            try
            {
                if (pollStatus != AlcoGpuAbi.Status.Ok)
                {
                    AlcoGpuMarshal.ThrowIfFailed(pollStatus);
                }

                wasMapped = true;
                void* pointer = GetMappedRange(readback.Buffer.Handle, readback.StagingDataSize);
                CopyCompletedTextureReadback(readback.Destination, pointer, readback);
                readback.Request.Complete();
                succeeded = true;
            }
            catch (Exception ex)
            {
                readback.Request.Fail(ex);
            }
            finally
            {
                if (wasMapped)
                {
                    AlcoGpuNative.BufferUnmap(Native, readback.Buffer.Handle);
                }
            }

            // A buffer may be returned to the cache only after a successful readback and unmap;
            // failures must destroy the native resource instead.
            if (succeeded)
            {
                ReturnStagingBuffer(readback.Buffer);
            }
            else
            {
                ReleaseReadbackBuffer(readback.Buffer);
            }

            _pendingTextureReadbacks.RemoveAt(i);
            i--;
        }
    }

    private void FailPendingTextureReadbacks(Exception error)
    {
        for (int i = 0; i < _pendingTextureReadbacks.Count; i++)
        {
            PendingTextureReadback readback = _pendingTextureReadbacks[i];
            readback.Request.Fail(error);
            ReleaseReadbackBuffer(readback.Buffer);
        }

        _pendingTextureReadbacks.Clear();
    }

    private void ReleaseReadbackBuffer(StagingTicket buffer)
    {
        if (buffer.Handle.IsNull)
        {
            return;
        }

        uint status = AlcoGpuNative.BufferDestroy(Native, buffer.Handle);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    /// <summary>
    /// Acquires a reusable staging buffer for a readback of <paramref name="requiredSize"/>
    /// bytes, creating a new native buffer only when no suitable idle buffer is cached. The
    /// returned buffer must later be passed to <see cref="ReturnStagingBuffer"/> or
    /// <see cref="ReleaseReadbackBuffer"/>. Native buffer creation is performed outside the
    /// staging-cache lock.
    /// </summary>
    private StagingTicket AcquireStagingBuffer(ulong requiredSize)
    {
        lock (_stagingCacheLock)
        {
            if (_stagingCache.TryAcquire(requiredSize, out StagingTicket cached))
            {
                return cached;
            }
        }

        // Miss: create a new buffer at a reuse-friendly bucket size. Done outside the lock.
        ulong capacity = _stagingCache.Bucketize(requiredSize);
        AlcoBufferDesc descriptor = new()
        {
            Size = capacity,
            Usage = (uint)(BufferUsage.MapRead | BufferUsage.CopyDst),
        };

        uint status = AlcoGpuNative.BufferCreate(Native, in descriptor, out AlcoHandle handle);
        AlcoGpuMarshal.ThrowIfFailed(status);
        return new StagingTicket(handle, capacity);
    }

    /// <summary>
    /// Returns a staging buffer to the cache after its mapped data has been copied and it has
    /// been unmapped, or destroys it when it is oversized or when trimming evicts it. This is
    /// the single return-or-destroy decision point for all readback paths. Native destroy is
    /// performed outside the staging-cache lock.
    /// </summary>
    private void ReturnStagingBuffer(StagingTicket buffer)
    {
        if (buffer.Handle.IsNull)
        {
            return;
        }

        if (!ShouldCacheStagingBuffer(buffer.Capacity))
        {
            ReleaseReadbackBuffer(buffer);
            return;
        }

        lock (_stagingCacheLock)
        {
            _stagingCache.Return(buffer, buffer.Capacity, Stopwatch.GetTimestamp(), _stagingCacheEvicted);
        }

        // Destroy any entries evicted by this return (expired or over-budget), outside the lock.
        DestroyEvicted();
    }

    /// <summary>
    /// Checks whether a buffer of <paramref name="capacity"/> is eligible for the idle cache
    /// without touching the cache state.
    /// </summary>
    private bool ShouldCacheStagingBuffer(ulong capacity)
    {
        return capacity <= StagingCacheSingleBufferMax;
    }

    /// <summary>
    /// Destroys every buffer whose ticket is currently in <see cref="_stagingCacheEvicted"/>
    /// and clears the list. Called outside the staging-cache lock.
    /// </summary>
    private void DestroyEvicted()
    {
        if (_stagingCacheEvicted.Count == 0)
        {
            return;
        }

        for (int i = 0; i < _stagingCacheEvicted.Count; i++)
        {
            ReleaseReadbackBuffer(_stagingCacheEvicted[i]);
        }

        _stagingCacheEvicted.Clear();
    }

    /// <summary>
    /// Runs idle-cache maintenance: trims expired and over-budget buffers. Called from
    /// <see cref="OnEndFrameCore"/> so idle memory is reclaimed even after readback activity
    /// has stopped. Native destroy of evicted buffers happens outside the lock.
    /// </summary>
    private void TrimStagingCache()
    {
        lock (_stagingCacheLock)
        {
            _stagingCache.Trim(Stopwatch.GetTimestamp(), _stagingCacheEvicted);
        }

        DestroyEvicted();
    }

    /// <summary>
    /// Retires finished texture readbacks and drains queued native messages. Idempotent and
    /// submit-free, so it is safe to call outside the frame loop (e.g. draining captures at
    /// shutdown) in addition to the end-of-frame call.
    /// </summary>
    protected unsafe override void ProcessPendingReadbacksCore()
    {
        if (_pendingTextureReadbacks.Count > 0)
        {
            ProcessPendingTextureReadbacks();
        }
    }

    protected unsafe override void OnEndFrameCore()
    {
        ProcessPendingReadbacksCore();
        PollMessages();

        // Trim the idle staging cache even when no readbacks are pending, so that the last
        // returned buffer does not stay cached forever after readback activity stops.
        int idleCount;
        lock (_stagingCacheLock)
        {
            idleCount = _stagingCache.IdleCount;
        }

        if (idleCount > 0)
        {
            TrimStagingCache();
        }
    }

    #endregion

    #region AlcoGpu Implementation

    /// <summary>
    /// Copies a buffer region into a staging buffer and submits it, returning the
    /// submission index the caller can poll/wait on.
    /// </summary>
    private ulong CopyBufferToStaging(AlcoHandle source, uint sourceOffset, AlcoHandle staging, ulong size)
    {
        AlcoHandle encoder = CreateEncoder("readback_encoder");
        uint status = AlcoGpuNative.CopyBufferToBuffer(Native, encoder, source, sourceOffset, staging, 0, size);
        if (status != AlcoGpuAbi.Status.Ok)
        {
            AlcoGpuNative.EncoderDestroy(Native, encoder);
            AlcoGpuMarshal.ThrowIfFailed(status);
        }

        return SubmitEncoder(encoder);
    }

    /// <summary>
    /// Copies a texture mip into a staging buffer and submits it, returning the
    /// submission index the caller can poll/wait on.
    /// </summary>
    private unsafe ulong CopyTextureToStaging(
        AlcoHandle source,
        uint mipLevel,
        AlcoHandle staging,
        in AlcoCopyLayout layout,
        AlcoExtent3D copySize)
    {
        AlcoHandle encoder = CreateEncoder("readback_encoder");
        uint status = AlcoGpuNative.CopyTextureToBuffer(
            Native, encoder, source, mipLevel, (uint)TextureAspect.All, staging, in layout, copySize);
        AlcoGpuMarshal.ThrowIfFailed(status);

        return SubmitEncoder(encoder);
    }

    private AlcoHandle CreateEncoder(string name)
    {
        ReadOnlySpan<byte> nameSpan = name.Utf8Z();
        fixed (byte* ptrName = nameSpan)
        {
            uint status = AlcoGpuNative.EncoderCreate(Native, ptrName, out AlcoHandle encoder);
            AlcoGpuMarshal.ThrowIfFailed(status);
            return encoder;
        }
    }

    private ulong SubmitEncoder(AlcoHandle encoder)
    {
        // finish consumes the encoder handle on both success and failure.
        uint status = AlcoGpuNative.EncoderFinish(Native, encoder, out AlcoHandle commandBuffer);
        AlcoGpuMarshal.ThrowIfFailed(status);

        lock (_textureUploadLock)
        {
            ulong index;
            status = AlcoGpuNative.QueueSubmit(Native, commandBuffer, &index);
            if (status != AlcoGpuAbi.Status.Ok)
            {
                AlcoGpuNative.CommandBufferDestroy(Native, commandBuffer);
                AlcoGpuMarshal.ThrowIfFailed(status);
            }

            return index;
        }
    }

    /// <summary>Blocks until the given submission (and its map callbacks) completes.</summary>
    private void PollAndWait(ulong submissionIndex)
    {
        uint status = AlcoGpuNative.DevicePoll(Native, AlcoGpuAbi.AlcoTrue, submissionIndex, null);
        AlcoGpuMarshal.ThrowIfFailed(status);
    }

    private void* GetMappedRange(AlcoHandle buffer, ulong size)
    {
        void* pointer = null;
        uint status = AlcoGpuNative.BufferGetMappedRange(Native, buffer, 0, size, &pointer);
        AlcoGpuMarshal.ThrowIfFailed(status);
        return pointer;
    }

    /// <summary>
    /// Drains the per-device message queue (validation errors, native warnings) and
    /// turns them into log calls or exceptions, mirroring the old wgpu log callback.
    /// </summary>
    private unsafe void PollMessages()
    {
        while (true)
        {
            AlcoDeviceMessage message = default;
            uint status = AlcoGpuNative.DevicePopMessage(Native, ref message);
            if (status != AlcoGpuAbi.Status.Ok)
            {
                break;
            }

            string text = AlcoGpuMarshal.BorrowedString(message.Message) ?? "<empty native message>";
            switch (message.Severity)
            {
                case 0: // error
                    throw new GraphicsException("[alco-gpu] " + text);
                case 1: // warning
                    if (IsDebug)
                    {
                        _host.LogWarning(text);
                    }
                    break;
                default:
                    _host.LogInfo(text);
                    break;
            }
        }
    }

    internal AlcoGpuDevice(in DeviceDescriptor descriptor) : base(descriptor)
    {
        IsDebug = descriptor.Debug;
        _descriptor = descriptor;
        _preferredSurfaceFormat = descriptor.PreferredSurfaceFormat;

        // All optional features are blanket-requested; the native side intersects
        // them with adapter support and reports the supported set back.
        GPUFeatures requestedFeatures =
            GPUFeatures.TextureCompressionBC
            | GPUFeatures.TimestampQuery
            | GPUFeatures.TimestampQueryInsidePasses
            | GPUFeatures.IndirectFirstInstance;

        ReadOnlySpan<byte> nameSpan = descriptor.Name.Utf8Z();
        fixed (byte* ptrName = nameSpan)
        {
            AlcoDeviceDesc desc = new()
            {
                Backend = BackendToRequest(descriptor.Backend),
                Debug = descriptor.Debug ? AlcoGpuAbi.AlcoTrue : AlcoGpuAbi.AlcoFalse,
                RequiredFeatures = (ulong)requestedFeatures,
                PushConstantsSize = descriptor.PushConstantsSize,
                Name = ptrName,
            };

            AlcoHandle deviceHandle;
            uint status = AlcoGpuNative.DeviceCreate(in desc, out deviceHandle);
            AlcoGpuMarshal.ThrowIfFailed(status);
            Native = deviceHandle;
        }

        AlcoDeviceInfo info = default;
        AlcoGpuMarshal.ThrowIfFailed(AlcoGpuNative.DeviceGetInfo(Native, ref info));

        Backend = info.Backend switch
        {
            AlcoGpuAbi.BackendResolved.Vulkan => GraphicsBackend.WGPUVulkan,
            AlcoGpuAbi.BackendResolved.Dx12 => GraphicsBackend.WGPUDx12,
            AlcoGpuAbi.BackendResolved.Metal => GraphicsBackend.WGPUMetal,
            _ => GraphicsBackend.Auto,
        };

        string adapterName = AlcoGpuMarshal.BorrowedString(info.AdapterName) ?? "unknown";
        _host.LogSuccess($"Adapter name: {adapterName}");
        _host.LogSuccess($"Graphics backend: {Backend}");

        _maxBindGroups = (int)info.MaxBindGroups;
        SupportedFeatures = (GPUFeatures)info.SupportedFeatures;

        if (SupportedFeatures.HasFlag(GPUFeatures.TextureCompressionBC))
        {
            _host.LogSuccess("Texture compression BC is supported");
        }
        if (SupportedFeatures.HasFlag(GPUFeatures.TimestampQuery))
        {
            _host.LogSuccess("GPU timestamp queries are supported");
        }
        if (SupportedFeatures.HasFlag(GPUFeatures.TimestampQueryInsidePasses))
        {
            _host.LogSuccess("GPU timestamp queries inside passes are supported");
        }
        if (!SupportedFeatures.HasFlag(GPUFeatures.IndirectFirstInstance))
        {
            _host.LogWarning(
                "Non-zero indirect firstInstance is unavailable; batched indirect draws that address per-draw data through firstInstance will not render correctly.");
        }

        ShaderPassthroughEnabled = (info.Caps & AlcoGpuAbi.Caps.PassthroughShaders) != 0;
        if (ShaderPassthroughEnabled)
        {
            _host.LogSuccess($"Native {Backend} shader passthrough is enabled");
        }
        else if (Backend == GraphicsBackend.WGPUVulkan)
        {
            _host.LogWarning("Native Vulkan SPIR-V passthrough is unavailable; using wgpu shader translation");
        }

        if (SupportedFeatures.HasFlag(GPUFeatures.MetalLibPassthrough))
        {
            _host.LogSuccess("Precompiled metallib shader passthrough is enabled");
        }

        TimestampPeriodNanoseconds = SupportedFeatures.HasFlag(GPUFeatures.TimestampQuery)
            ? info.TimestampPeriodNs
            : 0.0f;

        // create default bind groups
        BindGroupUniformBuffer = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_buffer",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.UniformBuffer),
            },
        });

        BindGroupStorageBuffer = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_storage_buffer",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.StorageBuffer),
            },
        });

        BindGroupStorageBufferWithCounter = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_storage_buffer_with_counter",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.StorageBuffer),
                new BindGroupEntry(1, ShaderStage.Standard, BindingType.StorageBuffer),
            },
        });

        BindGroupTexture3DRead = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_texture_3d_read",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture3D)),
            },
        });

        BindGroupTexture2DRead = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_texture_read",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, new TextureBindingInfo(TextureViewDimension.Texture2D)),
            },
        });

        BindGroupTexture2DStorage = CreateBindGroup(new BindGroupDescriptor
        {
            Name = "default_bind_group_storage_texture",
            Bindings = new BindGroupEntry[]
            {
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.StorageTexture, null, new StorageTextureBindingInfo(AccessMode.ReadWrite, TextureViewDimension.Texture2D, PixelFormat.RGBA8Unorm)),
            },
        });
    }

    private static uint BackendToRequest(GraphicsBackend backend)
    {
        return backend switch
        {
            GraphicsBackend.WGPUVulkan => AlcoGpuAbi.BackendRequest.Vulkan,
            GraphicsBackend.WGPUDx12 => AlcoGpuAbi.BackendRequest.Dx12,
            GraphicsBackend.WGPUMetal => AlcoGpuAbi.BackendRequest.Metal,
            _ => AlcoGpuAbi.BackendRequest.Auto,
        };
    }

    /// <summary>
    /// Logging channel reserved for internal alco-gpu object usage.
    /// </summary>
    internal void LogInfo(ReadOnlySpan<char> message)
    {
        _host.LogInfo(message);
    }

    internal void LogWarning(ReadOnlySpan<char> message)
    {
        _host.LogWarning(message);
    }

    #endregion
}
