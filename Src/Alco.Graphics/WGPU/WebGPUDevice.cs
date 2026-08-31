using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WebGPU;

using static WebGPU.WebGPU;

namespace Alco.Graphics.WebGPU;

internal sealed partial class WebGPUDevice : GPUDevice
{

    #region Properties

    public readonly WGPUInstance Instance;
    public readonly WGPUAdapter Adapter;
    public readonly WGPUDevice Device;
    public readonly WGPUQueue Queue;

    private const int MaxPendingTextureReadbacks = 16;

    // Staging-buffer cache constants. See Docs/Spec/2026-06-13-gpu-readback-staging-buffer-cache-design.md.
    private const ulong StagingCacheIdleBudget = 64UL * 1024 * 1024;
    private const ulong StagingCacheSingleBufferMax = 64UL * 1024 * 1024;
    private static readonly long _stagingCacheIdleExpirationTicks = (long)(10.0 * Stopwatch.Frequency); // 10 seconds
    private const ulong StagingCacheOversizeReuseThreshold = 4UL * 1024 * 1024;

    private readonly DeviceDescriptor _descriptor;
    private readonly List<PendingTextureReadback> _pendingTextureReadbacks = new(capacity: 4);
    private unsafe TextureReadbackCallbackState* _textureReadbackCallbackStates;
    // Native staging-buffer cache. The policy holds no native handles; the WGPUBuffer handle
    // is passed as the opaque ticket. All access is under _stagingCacheLock; native wgpu calls
    // (create/destroy/release) are made outside the lock.
    private readonly ReadbackStagingBufferCachePolicy<WGPUBuffer> _stagingCache =
        new(StagingCacheIdleBudget, StagingCacheSingleBufferMax, _stagingCacheIdleExpirationTicks, StagingCacheOversizeReuseThreshold);
    private readonly Lock _stagingCacheLock = new();
    private readonly List<WGPUBuffer> _stagingCacheEvicted = new(capacity: 4);

    // supported details
    private readonly PixelFormat _preferredSurfaceFormat;
    private readonly int _maxBindGroups;
    private GCHandle _thisHandle;

    public bool IsDebug { get; }

    /// <summary>
    /// Whether the device was created with wgpu's PassthroughShaders feature: slang's
    /// SPIR-V (Vulkan), DXIL (D3D12) and MSL/metallib (Metal) reach the backend as-is.
    /// Required for DXIL/MSL/MetalLib (no translation fallback exists); SPIR-V falls
    /// back to Naga import.
    /// </summary>
    internal bool ShaderPassthroughEnabled { get; }

    /// <summary>The backend the adapter actually selected (Auto resolves per platform).</summary>
    public override GraphicsBackend Backend { get; }

    #endregion

    private unsafe struct PendingTextureReadback
    {
        public GPUTextureReadbackRequest Request;
        public WGPUBuffer Buffer;
        public ulong StagingDataSize;
        public uint DataSize;
        public uint TightBytesPerRow;
        public uint AlignedBytesPerRow;
        public uint Height;
        public uint Depth;
        public byte* Destination;
        public int CallbackStateIndex;
    }

    private struct TextureReadbackCallbackState
    {
        public int IsInUse;
        public int IsCompleted;
        public WGPUMapAsyncStatus Status;
    }

    private struct TextureReadbackLayout
    {
        public WGPUTexelCopyBufferLayout BufferLayout;
        public WGPUExtent3D CopySize;
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
    /// The default bind group shared across the entire device.
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
    /// The maximum number of bind groups supported by the WebGPU adapter.
    /// </summary>
    public override int MaxBindGroups
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _maxBindGroups;
    }

    protected unsafe override void SubmitCore(GPUCommandBuffer commandBuffer)
    {
        WGPUCommandBuffer buffer = ((WebGPUCommandBuffer)commandBuffer).TakeBuffer();
        wgpuQueueSubmit(Queue, 1, &buffer);//add reference count
        wgpuCommandBufferRelease(buffer);//just decrement the reference count
    }

    protected unsafe override void DisposeCore()
    {
        FailPendingTextureReadbacks(
            new ObjectDisposedException(nameof(WebGPUDevice), "GPU device was disposed before texture readback completed."));
        if (_textureReadbackCallbackStates != null)
        {
            NativeMemory.Free(_textureReadbackCallbackStates);
            _textureReadbackCallbackStates = null;
        }

        // Release all idle cached staging buffers. Pending buffers were destroyed above by
        // FailPendingTextureReadbacks; idle buffers are drained here and destroyed natively.
        lock (_stagingCacheLock)
        {
            _stagingCache.Drain(_stagingCacheEvicted);
        }
        DestroyEvicted();

        //dispose default resources
        BindGroupUniformBuffer.Destroy();
        BindGroupStorageBuffer.Destroy();
        BindGroupStorageBufferWithCounter.Destroy();
        BindGroupTexture2DRead.Destroy();
        BindGroupTexture2DStorage.Destroy();

        DebugPrintReport();

        wgpuInstanceRelease(Instance);
        wgpuDeviceDestroy(Device);
        wgpuDeviceRelease(Device);
        wgpuAdapterRelease(Adapter);

        _thisHandle.Free();
    }

    protected override GPUBuffer CreateBufferCore(in BufferDescriptor descriptor)
    {
        return new WebGPUBuffer(this, descriptor);
    }

    protected override GPUTimestampQuerySet CreateTimestampQuerySetCore(uint count, string name)
    {
        return new WebGPUTimestampQuerySet(this, count, name);
    }

    protected override GPUCommandBuffer CreateCommandBufferCore(in CommandBufferDescriptor? descriptor = null)
    {
        return new WebGPUCommandBuffer(this, descriptor);
    }

    protected override GPURenderBundle CreateRenderBundleCore(in RenderBundleDescriptor? descriptor)
    {
        return new WebGPURenderBundle(this, descriptor);
    }

    protected override GPUTexture CreateTextureCore(in TextureDescriptor descriptor)
    {
        return new WebGPUTexture(this, descriptor);
    }

    protected override GPUAttachmentLayout CreateAttachmentLayoutCore(in AttachmentLayoutDescriptor descriptor)
    {
        return new WebGPUAttachmentLayout(this, descriptor);
    }

    protected override GPUFrameBuffer CreateFrameBufferCore(in FrameBufferDescriptor descriptor)
    {
        return new WebGPUFrameBuffer(this, descriptor);
    }

    protected override GPUFrameBuffer CreateExternalFrameBufferCore(in ExternalFrameBufferDescriptor descriptor)
    {
        return new WebGPUExternalFrameBuffer(this, descriptor);
    }

    protected override GPUPipeline CreateGraphicsPipelineCore(in GraphicsPipelineDescriptor descriptor)
    {
        return new WebGPUGraphicsPipeline(this, descriptor);
    }

    protected override GPUPipeline CreateComputePipelineCore(in ComputePipelineDescriptor descriptor)
    {
        return new WebGPUComputePipeline(this, descriptor);
    }

    protected override GPUBindGroup CreateBindGroupCore(in BindGroupDescriptor descriptor)
    {
        return new WebGPUBindGroup(this, descriptor);
    }

    protected override GPUResourceGroup CreateResourceGroupCore(in ResourceGroupDescriptor descriptor)
    {
        return new WebGPUResourceGroup(this, descriptor);
    }

    protected override GPUTextureView CreateTextureViewCore(in TextureViewDescriptor descriptor)
    {
        return new WebGPUTextureView(this, descriptor);
    }

    protected unsafe override GPUSampler CreateSamplerCore(in SamplerDescriptor descriptor)
    {
        return new WebGPUSampler(this, descriptor);
    }

    public override GPUSwapchain CreateSwapchainCore(in SwapchainDescriptor descriptor)
    {
        return new WebGPUSwapchain(this, descriptor);
    }

    protected override unsafe void WriteBufferCore(GPUBuffer buffer, uint bufferOffset, byte* data, uint size)
    {
        WGPUBuffer nativeBuffer = ((WebGPUBuffer)buffer).Native;
        wgpuQueueWriteBuffer(Queue, nativeBuffer, bufferOffset, data, size);
    }

    protected override unsafe void ReadBufferCore(GPUBuffer buffer, byte* dest, uint bufferOffset, uint size)
    {
        WGPUBuffer nativeBuffer = ((WebGPUBuffer)buffer).Native;
        WGPUBuffer tmpBuffer = AcquireStagingBuffer(size);
        bool succeeded = false;
        bool wasMapped = false;
        try
        {
            WGPUCommandEncoder encoder = wgpuDeviceCreateCommandEncoder(Device, null);
            wgpuCommandEncoderCopyBufferToBuffer(encoder, nativeBuffer, bufferOffset, tmpBuffer, 0, size);

            WGPUCommandBuffer commandBuffer = wgpuCommandEncoderFinish(encoder, null);

            ulong submissionIndex = wgpuQueueSubmitForIndex(Queue, 1, &commandBuffer);

            wgpuBufferMapAsync(tmpBuffer, WGPUMapMode.Read, 0, size,
                new WGPUBufferMapCallbackInfo()
                {
                    mode = (WGPUCallbackMode)0,
                    callback = &BufferMapCallback,
                    userdata1 = null,
                    userdata2 = null,
                });
            wgpuDevicePoll(Device, WGPUBool.True, &submissionIndex);
            wasMapped = true;

            void* pointer = wgpuBufferGetConstMappedRange(tmpBuffer, 0, size);
            Unsafe.CopyBlock(dest, pointer, size);

            wgpuCommandEncoderRelease(encoder);
            wgpuCommandBufferRelease(commandBuffer);
            succeeded = true;
        }
        finally
        {
            if (wasMapped)
            {
                wgpuBufferUnmap(tmpBuffer);
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
        WGPUTexture nativeTexture = ((WebGPUTexture)texture).Native;

        WGPUTexelCopyTextureInfo copyTextureInfo = new WGPUTexelCopyTextureInfo
        {
            texture = nativeTexture,
            mipLevel = mipLevel,
            origin = new WGPUOrigin3D
            {
                x = 0,
                y = 0,
                z = 0,
            },
            aspect = WGPUTextureAspect.All,
        };

        // The write covers exactly the given mip level's extent, not the level-0 size.
        uint mipWidth = Math.Max(1u, texture.Width >> (int)mipLevel);
        uint mipHeight = Math.Max(1u, texture.Height >> (int)mipLevel);

        WGPUTexelCopyBufferLayout textureDataLayout = WebGPUUtility.GetTextureDataLayout(texture.PixelFormat, mipWidth, mipHeight);

        WGPUExtent3D writeSize = new WGPUExtent3D
        {
            width = mipWidth,
            height = mipHeight,
            depthOrArrayLayers = texture.Depth,
        };

        wgpuQueueWriteTexture(Queue, &copyTextureInfo, data, dataSize, &textureDataLayout, &writeSize);
    }

    protected override unsafe void ReadTextureCore(GPUTexture texture, byte* dest, uint dataSize, uint mipLevel = 0)
    {
        // WebGPUTextureBase, not WebGPUTexture: swapchain surface textures are readable too.
        WGPUTexture nativeTexture = ((WebGPUTextureBase)texture).Native;
        TextureReadbackLayout layout = GetTextureReadbackLayout(texture, dataSize, mipLevel);

        WGPUBuffer tmpBuffer = WGPUBuffer.Null;
        WGPUCommandEncoder encoder = WGPUCommandEncoder.Null;
        WGPUCommandBuffer commandBuffer = WGPUCommandBuffer.Null;
        bool wasMapped = false;

        try
        {
            tmpBuffer = AcquireStagingBuffer(layout.StagingDataSize);

            WGPUTexelCopyTextureInfo source = new WGPUTexelCopyTextureInfo
            {
                texture = nativeTexture,
                mipLevel = mipLevel,
                origin = new WGPUOrigin3D
                {
                    x = 0,
                    y = 0,
                    z = 0,
                },
                aspect = WGPUTextureAspect.All,
            };

            WGPUTexelCopyBufferInfo destBuffer = new WGPUTexelCopyBufferInfo
            {
                buffer = tmpBuffer,
                layout = layout.BufferLayout,
            };

            encoder = wgpuDeviceCreateCommandEncoder(Device, null);
            wgpuCommandEncoderCopyTextureToBuffer(encoder, &source, &destBuffer, &layout.CopySize);

            commandBuffer = wgpuCommandEncoderFinish(encoder, null);
            ulong submissionIndex = wgpuQueueSubmitForIndex(Queue, 1, &commandBuffer);

            wgpuBufferMapAsync(tmpBuffer, WGPUMapMode.Read, 0, (nuint)layout.StagingDataSize,
                new WGPUBufferMapCallbackInfo()
                {
                    mode = (WGPUCallbackMode)0,
                    callback = &BufferMapCallback,
                    userdata1 = null,
                    userdata2 = null,
            });
            wgpuDevicePoll(Device, WGPUBool.True, &submissionIndex);

            wasMapped = true;
            void* pointer = wgpuBufferGetConstMappedRange(tmpBuffer, 0, (nuint)layout.StagingDataSize);
            CopyCompletedTextureReadback(dest, pointer, layout);
        }
        catch
        {
            // On any failure the buffer must not be returned to the cache; unmap (if needed)
            // and destroy it instead.
            if (wasMapped && tmpBuffer.IsNotNull)
            {
                wgpuBufferUnmap(tmpBuffer);
                wasMapped = false;
            }

            if (tmpBuffer.IsNotNull)
            {
                ReleaseReadbackBuffer(tmpBuffer);
                tmpBuffer = WGPUBuffer.Null;
            }

            throw;
        }
        finally
        {
            if (wasMapped)
            {
                wgpuBufferUnmap(tmpBuffer);
            }

            if (commandBuffer.IsNotNull)
            {
                wgpuCommandBufferRelease(commandBuffer);
            }

            if (encoder.IsNotNull)
            {
                wgpuCommandEncoderRelease(encoder);
            }

            if (tmpBuffer.IsNotNull)
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
        WGPUTexture nativeTexture = ((WebGPUTextureBase)texture).Native;
        TextureReadbackLayout layout = GetTextureReadbackLayout(texture, dataSize, mipLevel);

        WGPUBuffer tmpBuffer = WGPUBuffer.Null;
        WGPUCommandEncoder encoder = WGPUCommandEncoder.Null;
        WGPUCommandBuffer commandBuffer = WGPUCommandBuffer.Null;
        int callbackStateIndex = -1;

        try
        {
            callbackStateIndex = AcquireTextureReadbackCallbackState();
            TextureReadbackCallbackState* callbackState = _textureReadbackCallbackStates + callbackStateIndex;

            tmpBuffer = AcquireStagingBuffer(layout.StagingDataSize);

            WGPUTexelCopyTextureInfo source = new WGPUTexelCopyTextureInfo
            {
                texture = nativeTexture,
                mipLevel = mipLevel,
                origin = new WGPUOrigin3D
                {
                    x = 0,
                    y = 0,
                    z = 0,
                },
                aspect = WGPUTextureAspect.All,
            };

            WGPUTexelCopyBufferInfo destBuffer = new WGPUTexelCopyBufferInfo
            {
                buffer = tmpBuffer,
                layout = layout.BufferLayout,
            };

            encoder = wgpuDeviceCreateCommandEncoder(Device, null);
            wgpuCommandEncoderCopyTextureToBuffer(encoder, &source, &destBuffer, &layout.CopySize);

            commandBuffer = wgpuCommandEncoderFinish(encoder, null);
            wgpuQueueSubmit(Queue, 1, &commandBuffer);

            wgpuBufferMapAsync(tmpBuffer, WGPUMapMode.Read, 0, (nuint)layout.StagingDataSize,
                new WGPUBufferMapCallbackInfo()
                {
                    mode = WGPUCallbackMode.AllowProcessEvents,
                    callback = &TextureReadbackMapCallback,
                    userdata1 = callbackState,
                    userdata2 = null,
                });

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
                CallbackStateIndex = callbackStateIndex,
            });
            callbackStateIndex = -1;
            tmpBuffer = WGPUBuffer.Null;
        }
        finally
        {
            if (commandBuffer.IsNotNull)
            {
                wgpuCommandBufferRelease(commandBuffer);
            }

            if (encoder.IsNotNull)
            {
                wgpuCommandEncoderRelease(encoder);
            }

            if (tmpBuffer.IsNotNull)
            {
                ReleaseReadbackBuffer(tmpBuffer);
            }

            if (callbackStateIndex >= 0)
            {
                ReleaseTextureReadbackCallbackState(callbackStateIndex);
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

        WGPUTexelCopyBufferLayout textureDataLayout;
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

            textureDataLayout = new WGPUTexelCopyBufferLayout
            {
                offset = 0,
                bytesPerRow = alignedBytesPerRow,
                rowsPerImage = height,
            };
            stagingDataSize = (ulong)alignedBytesPerRow * height * depth;
        }
        else
        {
            textureDataLayout = WebGPUUtility.GetTextureDataLayout(texture.PixelFormat, width, height);
        }

        return new TextureReadbackLayout
        {
            BufferLayout = textureDataLayout,
            CopySize = new WGPUExtent3D
            {
                width = width,
                height = height,
                depthOrArrayLayers = depth,
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
            throw new GraphicsException("WebGPU texture readback returned a null mapped range.");
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
            throw new GraphicsException("WebGPU texture readback returned a null mapped range.");
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
        for (int i = 0; i < _pendingTextureReadbacks.Count; i++)
        {
            PendingTextureReadback readback = _pendingTextureReadbacks[i];
            TextureReadbackCallbackState* callbackState = _textureReadbackCallbackStates + readback.CallbackStateIndex;
            if (callbackState->IsCompleted == 0)
            {
                continue;
            }

            bool succeeded = false;
            bool wasMapped = false;
            try
            {
                if (callbackState->Status != WGPUMapAsyncStatus.Success)
                {
                    throw new GraphicsException($"WebGPU texture readback map failed. Status: {callbackState->Status}.");
                }

                wasMapped = true;
                void* pointer = wgpuBufferGetConstMappedRange(readback.Buffer, 0, (nuint)readback.StagingDataSize);
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
                    wgpuBufferUnmap(readback.Buffer);
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

            ReleaseTextureReadbackCallbackState(readback.CallbackStateIndex);
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
            ReleaseTextureReadbackCallbackState(readback.CallbackStateIndex);
        }

        _pendingTextureReadbacks.Clear();
    }

    private static void ReleaseReadbackBuffer(WGPUBuffer buffer)
    {
        if (buffer.IsNull)
        {
            return;
        }

        wgpuBufferDestroy(buffer);
        wgpuBufferRelease(buffer);
    }

    /// <summary>
    /// Acquires a reusable staging buffer for a readback of <paramref name="requiredSize"/>
    /// bytes, creating a new native buffer only when no suitable idle buffer is cached. The
    /// returned buffer must later be passed to <see cref="ReturnStagingBuffer"/> or
    /// <see cref="ReleaseReadbackBuffer"/>. Native buffer creation is performed outside the
    /// staging-cache lock.
    /// </summary>
    private unsafe WGPUBuffer AcquireStagingBuffer(ulong requiredSize)
    {
        WGPUBuffer cached = WGPUBuffer.Null;
        lock (_stagingCacheLock)
        {
            if (_stagingCache.TryAcquire(requiredSize, out cached))
            {
                return cached;
            }
        }

        // Miss: create a new buffer at a reuse-friendly bucket size. Done outside the lock.
        ulong capacity = _stagingCache.Bucketize(requiredSize);
        WGPUBufferDescriptor descriptor = new WGPUBufferDescriptor
        {
            size = capacity,
            usage = WGPUBufferUsage.MapRead | WGPUBufferUsage.CopyDst,
            mappedAtCreation = false,
        };
        return wgpuDeviceCreateBuffer(Device, &descriptor);
    }

    /// <summary>
    /// Returns a staging buffer to the cache after its mapped data has been copied and it has
    /// been unmapped, or destroys it when it is oversized or when trimming evicts it. This is
    /// the single return-or-destroy decision point for all readback paths. Native destroy is
    /// performed outside the staging-cache lock.
    /// </summary>
    private void ReturnStagingBuffer(WGPUBuffer buffer)
    {
        if (buffer.IsNull)
        {
            return;
        }

        // Query the native size outside the lock; it is a read-only handle attribute.
        ulong capacity = wgpuBufferGetSize(buffer);
        bool oversized = !ShouldCacheStagingBuffer(capacity);

        if (oversized)
        {
            wgpuBufferDestroy(buffer);
            wgpuBufferRelease(buffer);
            return;
        }

        lock (_stagingCacheLock)
        {
            _stagingCache.Return(buffer, capacity, Stopwatch.GetTimestamp(), _stagingCacheEvicted);
        }

        // Destroy any entries evicted by this return (expired or over-budget), outside the lock.
        DestroyEvicted();
    }

    /// <summary>
    /// Checks whether a buffer of <paramref name="capacity"/> is eligible for the idle cache
    /// without touching the cache state. Exposed so <see cref="ReturnStagingBuffer"/> can decide
    /// before acquiring the lock.
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
            WGPUBuffer evicted = _stagingCacheEvicted[i];
            if (evicted.IsNotNull)
            {
                wgpuBufferDestroy(evicted);
                wgpuBufferRelease(evicted);
            }
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

    private unsafe int AcquireTextureReadbackCallbackState()
    {
        TextureReadbackCallbackState* states = _textureReadbackCallbackStates;
        for (int i = 0; i < MaxPendingTextureReadbacks; i++)
        {
            if (states[i].IsInUse != 0)
            {
                continue;
            }

            states[i] = new TextureReadbackCallbackState
            {
                IsInUse = 1,
                Status = WGPUMapAsyncStatus.None,
            };
            return i;
        }

        throw new GraphicsException($"Cannot start more than {MaxPendingTextureReadbacks} pending texture readbacks.");
    }

    private unsafe void ReleaseTextureReadbackCallbackState(int index)
    {
        if (index < 0)
        {
            return;
        }

        _textureReadbackCallbackStates[index] = default;
    }

    protected unsafe override void OnEndFrameCore()
    {
        // Drain completed async readbacks, and pump event processing only when there are any.
        if (_pendingTextureReadbacks.Count > 0)
        {
            wgpuInstanceProcessEvents(Instance);
            ProcessPendingTextureReadbacks();
        }

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

    #region WebGPU Implementation

    public WGPUDevice Native
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Device;
    }

    public unsafe WebGPUDevice(in DeviceDescriptor descriptor) : base(descriptor)
    {
        _thisHandle = GCHandle.Alloc(this, GCHandleType.Normal);
        IsDebug = descriptor.Debug;

        _descriptor = descriptor;
        _textureReadbackCallbackStates = (TextureReadbackCallbackState*)NativeMemory.AllocZeroed(
            MaxPendingTextureReadbacks,
            (nuint)sizeof(TextureReadbackCallbackState));
        wgpuSetLogCallback(LogCallback, GCHandle.ToIntPtr(_thisHandle));

        _preferredSurfaceFormat = descriptor.PreferredSurfaceFormat;

        // create instance
        WGPUInstanceExtras extras = new WGPUInstanceExtras()
        {
            chain = new WGPUChainedStruct
            {
                sType = (WGPUSType)WGPUNativeSType.InstanceExtras,
                next = null,
            },
            flags = descriptor.Debug ? WGPUInstanceFlag.Validation : WGPUInstanceFlag.Default,
            backends = WebGPUUtility.BackendToWebGPU(descriptor.Backend),
        };

        WGPUInstanceDescriptor instanceDescriptor = new WGPUInstanceDescriptor()
        {
            nextInChain = (WGPUChainedStruct*)&extras,
        };

        Instance = wgpuCreateInstance(&instanceDescriptor);

        // create adapter
        WGPURequestAdapterOptions requestAdapterOptions = new WGPURequestAdapterOptions()
        {
            nextInChain = null,
            //compatibleSurface = Surface,
            powerPreference = WGPUPowerPreference.HighPerformance,
            backendType = WebGPUUtility.BackendTypeToWebGPU(descriptor.Backend),
        };

        WGPUAdapter adapter = WGPUAdapter.Null;
        wgpuInstanceRequestAdapter(
            Instance,
            &requestAdapterOptions,
            new WGPURequestAdapterCallbackInfo()
            {
                callback = &OnAdapterRequestEnded,
                userdata1 = &adapter,
                userdata2 = null,
            });
        Adapter = adapter;

        WGPUAdapterInfo info = default;
        wgpuAdapterGetInfo(adapter, &info);
        WGPUBackendType backendType = info.backendType;
        Backend = backendType switch
        {
            WGPUBackendType.Vulkan => GraphicsBackend.WGPUVulkan,
            WGPUBackendType.D3D12 => GraphicsBackend.WGPUDx12,
            WGPUBackendType.Metal => GraphicsBackend.WGPUMetal,
            _ => GraphicsBackend.Auto,
        };
        _host.LogSuccess($"Adapter name: {info.device}");
        _host.LogSuccess($"Graphics backend: {info.backendType}");

        wgpuAdapterInfoFreeMembers(info);

        ReadOnlySpan<WGPUFeatureName> supportedFeatures = wgpuAdapterEnumerateFeatures(Adapter);

        GPUFeatures gpuFeatures = GPUFeatures.None;

        List<WGPUFeatureName> featuresList = new List<WGPUFeatureName>(){
            (WGPUFeatureName)WGPUNativeFeature.VertexWritableStorage
        };

        if (!ContainsFeature((WGPUFeatureName)WGPUNativeFeature.Immediates, supportedFeatures))
        {
            throw new GraphicsException("Push constants (immediates) are not supported which is required");
        }

        if(ContainsFeature(WGPUFeatureName.TextureCompressionBC, supportedFeatures))
        {
            gpuFeatures |= GPUFeatures.TextureCompressionBC;
            featuresList.Add(WGPUFeatureName.TextureCompressionBC);
            _host.LogSuccess("Texture compression BC is supported");
        }

        if (ContainsFeature(WGPUFeatureName.TimestampQuery, supportedFeatures))
        {
            gpuFeatures |= GPUFeatures.TimestampQuery;
            featuresList.Add(WGPUFeatureName.TimestampQuery);
            _host.LogSuccess("GPU timestamp queries are supported");
        }
        if (gpuFeatures.HasFlag(GPUFeatures.TimestampQuery)
            && ContainsFeature((WGPUFeatureName)WGPUNativeFeature.TimestampQueryInsidePasses, supportedFeatures))
        {
            gpuFeatures |= GPUFeatures.TimestampQueryInsidePasses;
            featuresList.Add((WGPUFeatureName)WGPUNativeFeature.TimestampQueryInsidePasses);
            _host.LogSuccess("GPU timestamp queries inside passes are supported");
        }

        // Non-zero firstInstance in indirect records: required by batched indirect
        // draws that address their per-draw data through firstInstance (see
        // GpuParticleSystem2D). This is a real optional WebGPU feature and must be
        // requested explicitly.
        if (ContainsFeature(WGPUFeatureName.IndirectFirstInstance, supportedFeatures))
        {
            gpuFeatures |= GPUFeatures.IndirectFirstInstance;
            featuresList.Add(WGPUFeatureName.IndirectFirstInstance);
        }
        else
        {
            _host.LogWarning(
                "Non-zero indirect firstInstance is unavailable; batched indirect draws that address per-draw data through firstInstance will not render correctly.");
        }

        // Multi-draw indirect with a CPU-known count needs no feature: wgpu
        // executes multi_draw_indirect natively where the hardware supports it
        // and emulates it (batching single draws) elsewhere, so the multi-draw
        // entry points are always callable. The MULTI_DRAW_INDIRECT_COUNT
        // feature only marks the counted variants as non-emulated, which the
        // batched draws do not use.
        gpuFeatures |= GPUFeatures.MultiDrawIndirect;

        if (backendType == WGPUBackendType.Vulkan)
        {
            WGPUFeatureName passthroughShaders = (WGPUFeatureName)WGPUNativeFeature.PassthroughShaders;
            if (ContainsFeature(passthroughShaders, supportedFeatures))
            {
                ShaderPassthroughEnabled = true;
                featuresList.Add(passthroughShaders);
                _host.LogSuccess("Native Vulkan SPIR-V shader passthrough is enabled");
            }
            else
            {
                _host.LogWarning(
                    "Native Vulkan SPIR-V passthrough is unavailable; using wgpu shader translation");
            }
        }
        else if (backendType == WGPUBackendType.D3D12 || backendType == WGPUBackendType.Metal)
        {
            // Slang emits DXIL for D3D12 and MSL (or precompiled metallib) for
            // Metal; wgpu can only consume them through passthrough, so the
            // feature is required, not optional.
            WGPUFeatureName passthroughShaders = (WGPUFeatureName)WGPUNativeFeature.PassthroughShaders;
            if (!ContainsFeature(passthroughShaders, supportedFeatures))
            {
                throw new GraphicsException(
                    $"{backendType} requires wgpu's PassthroughShaders feature for slang DXIL/MSL shaders, " +
                    "but the adapter or wgpu-native build does not expose it. " +
                    "Use a wgpu-native build with the Alco passthrough patch (see the alco-wgpu-native overlay repository).");
            }
            ShaderPassthroughEnabled = true;
            featuresList.Add(passthroughShaders);
            _host.LogSuccess($"Native {backendType} shader passthrough is enabled");

            if (backendType == WGPUBackendType.Metal)
            {
                // wgpuGetProcAddress is an unimplemented stub upstream (it
                // panics), so probe the loaded library's export table instead —
                // an older build keeps the MSL source path. The path-aware
                // load also returns the already-loaded DllImport handle.
                if (WGPUNativeLibrary.TryLoad(out nint library)
                    && NativeLibrary.TryGetExport(library, "wgpuDeviceCreateShaderModuleMetalLib", out _))
                {
                    gpuFeatures |= GPUFeatures.MetalLibPassthrough;
                    _host.LogSuccess("Precompiled metallib shader passthrough is enabled");
                }
            }
        }


        SupportedFeatures = gpuFeatures;

        featuresList.Add((WGPUFeatureName)WGPUNativeFeature.Immediates);
        featuresList.Add((WGPUFeatureName)WGPUNativeFeature.TextureAdapterSpecificFormatFeatures);

        WGPUFeatureName* features = stackalloc WGPUFeatureName[featuresList.Count];
        for (int i = 0; i < featuresList.Count; i++)
        {
            features[i] = featuresList[i];
        }

        // create device
        ReadOnlySpan<byte> nameSpan = descriptor.Name.GetUtf8Span();
        fixed (byte* name = nameSpan)
        {


            WGPULimits limits = default;

            WGPUStatus status = wgpuAdapterGetLimits(Adapter, &limits);
            if(status != WGPUStatus.Success)
            {
                throw new GraphicsException("Could not get WebGPU adapter limits");
            }
            _maxBindGroups = (int)limits.maxBindGroups;

            // wgpu v29: push constants became immediates, a single block sized via the standard limits.
            limits.maxImmediateSize = descriptor.PushConstantsSize;

            WGPUDeviceDescriptor deviceDescriptor = new WGPUDeviceDescriptor()
            {
                nextInChain = null,
                label = new WGPUStringView(name, nameSpan.Length),
                requiredLimits = &limits,
                requiredFeatureCount = (uint)featuresList.Count,
                requiredFeatures = features,
                uncapturedErrorCallbackInfo = new WGPUUncapturedErrorCallbackInfo
                {
                    nextInChain = null,
                    callback = &ErrorCallback,
                    userdata1 = null,
                    userdata2 = null,
                },
            };

            deviceDescriptor.defaultQueue.nextInChain = null;

            WGPUDevice device = WGPUDevice.Null;
            wgpuAdapterRequestDevice(
                Adapter,
                &deviceDescriptor,
                new WGPURequestDeviceCallbackInfo()
                {
                    callback = &OnDeviceRequestEnded,
                    userdata1 = &device,
                    userdata2 = null,
                });
            Device = device;
        }

        //wgpuDeviceSetUncapturedErrorCallback(Device, &OnUnhandleError);

        //get queue
        Queue = wgpuDeviceGetQueue(Device);
        TimestampPeriodNanoseconds = IsFeatureSupported(GPUFeatures.TimestampQuery) ? wgpuQueueGetTimestampPeriod(Queue) : 0.0f;
        
        //create default bind groups
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
                new BindGroupEntry(0, ShaderStage.Standard, BindingType.StorageTexture, null, new StorageTextureBindingInfo(AccessMode.ReadWrite, TextureViewDimension.Texture2D,PixelFormat.RGBA8Unorm)),
            },
        });
    }


    #endregion

    #region Callbacks

    [UnmanagedCallersOnly]
    private unsafe static void OnAdapterRequestEnded(WGPURequestAdapterStatus status, WGPUAdapter candidateAdapter, WGPUStringView message, void* pUserData1, void* pUserData2)
    {
        if (status == WGPURequestAdapterStatus.Success)
        {
            *(WGPUAdapter*)pUserData1 = candidateAdapter;
        }
        else
        {
            throw new GraphicsException("Could not get WebGPU adapter: " + message.ToString());
        }
    }

    [UnmanagedCallersOnly]
    private unsafe static void OnDeviceRequestEnded(WGPURequestDeviceStatus status, WGPUDevice device, WGPUStringView message, void* pUserData1, void* pUserData2)
    {
        if (status == WGPURequestDeviceStatus.Success)
        {
            *(WGPUDevice*)pUserData1 = device;
        }
        else
        {
            throw new GraphicsException("Could not get WebGPU device: " + message.ToString());
        }
    }

    [UnmanagedCallersOnly]
    private unsafe static void OnUnhandleError(WGPUErrorType type, byte* message, void* pUserData)
    {
        throw new GraphicsException("Unhandle WebGPU error: " + Interop.GetString(message));
    }

    [UnmanagedCallersOnly]
    private unsafe static void BufferMapCallback(WGPUMapAsyncStatus status, WGPUStringView message, void* userdata1, void* userdata2)
    {
        if (status != WGPUMapAsyncStatus.Success)
        {
            throw new GraphicsException("WebGPU buffer map failed: " + message.ToString());
        }
    }

    [UnmanagedCallersOnly]
    private unsafe static void TextureReadbackMapCallback(WGPUMapAsyncStatus status, WGPUStringView message, void* userdata1, void* userdata2)
    {
        if (userdata1 == null)
        {
            return;
        }

        TextureReadbackCallbackState* state = (TextureReadbackCallbackState*)userdata1;
        state->Status = status;
        state->IsCompleted = 1;
    }

    [UnmanagedCallersOnly]
    private static unsafe void ErrorCallback(WGPUDevice* device, WGPUErrorType type, WGPUStringView message, void* userdata1, void* userdata2)
    {
        throw new GraphicsException("WebGPU error: " + message.ToString());
    }

    private static void LogCallback(WGPULogLevel level, string message, nint userdata = 0)
    {
        WebGPUDevice device = (WebGPUDevice)GCHandle.FromIntPtr(userdata).Target!;
        switch (level)
        {
            case WGPULogLevel.Error:
                throw new GraphicsException(message);
            case WGPULogLevel.Warn:
                if (device.IsDebug)
                {
                    device._host.LogWarning(message);
                }
                break;
            case WGPULogLevel.Info:
            case WGPULogLevel.Debug:
            case WGPULogLevel.Trace:
                device._host.LogInfo(message);
                break;
        }
    }

    private unsafe static bool ContainsFeature(WGPUFeatureName feature, ReadOnlySpan<WGPUFeatureName> supportedFeatures)
    {
        for (int i = 0; i < supportedFeatures.Length; i++)
        {
            if (supportedFeatures[i] == feature)
            {
                return true;
            }
        }
        return false;
    }


    /// <summary>
    /// Logging channel reserved for internal wgpu object usage.
    /// </summary>
    internal void LogInfo(ReadOnlySpan<char> message){
        _host.LogInfo(message);
    }

    internal void LogWarning(ReadOnlySpan<char> message){
        _host.LogWarning(message);
    }
    
    [Conditional("DEBUG")]
    private unsafe void DebugPrintReport()
    {
        // WGPUGlobalReport report;
        // wgpuGenerateReport(Instance, &report);

        // switch(report.backendType)
        // {
        //     case WGPUBackendType.Vulkan:
        //         PrintHubReport(report.vulkan);
        //         break;
        //     case WGPUBackendType.Metal:
        //         PrintHubReport(report.metal);
        //         break;
        //     case WGPUBackendType.D3D12:
        //         PrintHubReport(report.dx12);
        //         break;
        //     case WGPUBackendType.OpenGLES:
        //     case WGPUBackendType.OpenGL:
        //         PrintHubReport(report.gl);
        //         break;
        //     case WGPUBackendType.WebGPU:
        //     default:
        //         break;
        // }
    }

    private void PrintHubReport(WGPUHubReport report)
    {
        _host.LogInfo("Hub report:");
        PrintRegistryReport(report.adapters, "adapters");
        PrintRegistryReport(report.devices, "devices");
        PrintRegistryReport(report.queues, "queues");
        PrintRegistryReport(report.pipelineLayouts, "pipelineLayouts");
        PrintRegistryReport(report.shaderModules, "shaderModules");
        PrintRegistryReport(report.bindGroupLayouts, "bindGroupLayouts");
        PrintRegistryReport(report.bindGroups, "bindGroups");
        PrintRegistryReport(report.commandBuffers, "commandBuffers");
        PrintRegistryReport(report.renderBundles, "renderBundles");
        PrintRegistryReport(report.renderPipelines, "renderPipelines");
        PrintRegistryReport(report.computePipelines, "computePipelines");
        PrintRegistryReport(report.querySets, "querySets");
        PrintRegistryReport(report.buffers, "buffers");
        PrintRegistryReport(report.textures, "textures");
        PrintRegistryReport(report.textureViews, "textureViews");
        PrintRegistryReport(report.samplers, "samplers");
    }

    private void PrintRegistryReport(WGPURegistryReport report, string name)
    {
        _host.LogInfo($"Registry report for {name}:");
        _host.LogInfo($"  Element size: {report.elementSize}");
        _host.LogInfo($"  Allocated: {report.numAllocated}");
        _host.LogInfo($"  Kept from user: {report.numKeptFromUser}");
        _host.LogInfo($"  Released from user: {report.numReleasedFromUser}");
    }

    #endregion
}
