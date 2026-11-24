// Example-level frame recording and submission with render and compute scopes and per-frame storage.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Owns two queue-zero recording slots; each concurrent recording task needs its own context.</summary>
/// <remarks>Serialize submissions with all other queue-zero operations that submit work.</remarks>
public sealed class RenderContext : RenderingResource
{
    private struct OverflowPage
    {
        internal GPUHeap Heap;
        internal BumpAllocator Allocator;
        internal Buffer Buffer;
        internal ulong CapacityBytes;
        internal ulong BaseOffset;
    }

    private sealed class FrameSlot
    {
        internal readonly GPUCommandPool Pool;
        internal readonly BumpAllocator RootData;
        internal readonly Buffer MainBuffer;
        internal readonly ulong MainBaseOffset;
        internal List<OverflowPage>? OverflowPages;
        internal GPUHeap UploadHeap;
        internal BumpAllocator? UploadData;
        internal TimelinePoint Completion;
        internal ulong SubmissionValue;
        internal ulong FrameId;

        internal FrameSlot(RenderingSystem system, GPUAllocation<byte> storage, Buffer mainBuffer)
        {
            RootData = new BumpAllocator(storage);
            MainBuffer = mainBuffer;
            MainBaseOffset = storage.Offset;
            GPUCommandPool pool = system.Device.CreateCommandPool(0);
            try
            {
                GPUCommandBuffer commands = pool.BeginCommands();
                commands.EndCommands();
                pool.Reset();
            }
            catch (GPUException)
            {
                pool.Destroy();
                throw;
            }
            Pool = pool;
        }
    }

    private enum ScopeKind : byte
    {
        None,
        Render,
        PreparedRender,
        Compute,
    }

    private const Access TextureAccess = Access.ShaderRead | Access.ShaderWrite | Access.TransferRead | Access.TransferWrite
        | Access.ColorRead | Access.ColorWrite | Access.DepthStencilRead | Access.DepthStencilWrite;

    private readonly GPUHeap _rootHeap;
    private readonly FrameSlot[] _frames;
    private readonly ulong _frameDataByteCount;
    private readonly ulong _uploadByteCount;
    private int _frameIndex;
    private ulong _nextFrameId;
    private ulong _activeFrameId;
    private ulong _discardVersion;
    private int _recordedTimestampCount;
    private GPUCommandBuffer? _commands;
    private bool _recording;
    private ScopeKind _scope;
    private uint _scopeId;
    private RenderPassSignature _passSignature;
    private ShaderVariant? _lastVariant;
    private PipelineStateKey _lastStateKey;
    private GPUPipeline? _lastPipeline;
    private DepthStencilState LastDepthStencil;
    private bool HasDepthStencil;
    private ulong _recordingId;

    /// <summary>Creates and prewarms two command pools with independent root storage; failed creation releases partial resources.</summary>
    /// <param name="system">The rendering system supplying the device and descriptor heaps.</param>
    /// <param name="frameDataByteCount">The root storage capacity per slot, a positive multiple of 16 bytes.</param>
    /// <param name="uploadByteCount">The lazily allocated upload capacity per slot, a positive multiple of 16 bytes.</param>
    /// <exception cref="GPUException">Command or root storage could not be created.</exception>
    public RenderContext(RenderingSystem system, ulong frameDataByteCount = 64 * 1024, ulong uploadByteCount = 4 * 1024 * 1024) : base(system)
    {
        Debug.Assert(frameDataByteCount != 0 && frameDataByteCount % BumpAllocator.Alignment == 0);
        Debug.Assert(uploadByteCount != 0 && uploadByteCount % BumpAllocator.Alignment == 0);
        _frameDataByteCount = frameDataByteCount;
        _uploadByteCount = uploadByteCount;
        GPUHeap rootHeap = system.Device.CreateGPUHeap(frameDataByteCount * 2);
        FrameSlot[] frames = new FrameSlot[2];
        BumpAllocator storage = new(rootHeap.Range);
        int frameCount = 0;
        try
        {
            for (; frameCount < frames.Length; ++frameCount)
            {
                GPUAllocation<byte> slotStorage = storage.Allocate(frameDataByteCount);
                Buffer mainBuffer = new(system, slotStorage, MemoryType.CpuVisible);
                frames[frameCount] = new FrameSlot(system, slotStorage, mainBuffer);
            }
        }
        catch (GPUException)
        {
            for (int index = frameCount - 1; index >= 0; --index)
                frames[index].Pool.Destroy();
            rootHeap.Destroy();
            GC.SuppressFinalize(this);
            throw;
        }
        _rootHeap = rootHeap;
        _frames = frames;
    }

    private GPUCommandBuffer RecordingCommands
    {
        get
        {
            Debug.Assert(!IsDisposed && _recording && _commands != null);
            return _commands;
        }
    }

    internal GPUCommandBuffer CommandsBetweenScopes
    {
        get
        {
            Debug.Assert(_scope == ScopeKind.None);
            return RecordingCommands;
        }
    }

    internal RenderingSystem System => _system;

    internal ulong DetachActiveRecording() => Interlocked.Exchange(ref _recordingId, 0);

    /// <summary>Monotonic identifier of the active recording frame, or zero when no frame is recording.</summary>
    public ulong CurrentFrameId => _activeFrameId;

    /// <summary>Monotonic version incremented whenever an unsubmitted frame is discarded.</summary>
    public ulong DiscardVersion => _discardVersion;

    /// <summary>Number of timestamps recorded in the active frame.</summary>
    public int TimestampCount => _recordedTimestampCount;

    /// <summary>Reports whether <paramref name="frameId"/> is still recording or awaiting slot reclamation and timestamp collection.</summary>
    /// <param name="frameId">A frame identifier previously read from <see cref="CurrentFrameId"/>.</param>
    /// <returns><see langword="true"/> while the frame's slot and recorded timestamp destinations remain in use.</returns>
    public bool IsFramePending(ulong frameId)
    {
        if (frameId == 0 || IsDisposed)
            return false;
        for (int index = 0; index < _frames.Length; ++index)
        {
            if (_frames[index].FrameId == frameId)
                return true;
        }
        return false;
    }

    /// <summary>Reports whether GPU work for <paramref name="frameId"/> has completed, was discarded, or belongs to a destroyed context.</summary>
    /// <param name="frameId">A frame identifier previously read from <see cref="CurrentFrameId"/>.</param>
    /// <returns><see langword="true"/> when resources used only by <paramref name="frameId"/> are safe to destroy without blocking.</returns>
    public bool IsFrameCompleted(ulong frameId)
    {
        if (frameId == 0 || IsDisposed)
            return true;
        if (frameId == _activeFrameId)
            return false;
        for (int index = 0; index < _frames.Length; ++index)
        {
            FrameSlot frame = _frames[index];
            if (frame.FrameId != frameId)
                continue;
            return frame.Completion.Semaphore != null &&
                frame.Completion.Semaphore.CompletedValue >= frame.Completion.Value;
        }
        return true;
    }

    [InlineArray((int)Backend.MaxColorAttachments)]
    private struct GPUColorAttachmentArray
    {
        private GPUColorAttachment _element0;
    }

    private bool HasRenderPass => _scope is ScopeKind.Render or ScopeKind.PreparedRender;

    internal BumpAllocator FrameData
    {
        get
        {
            Debug.Assert(!IsDisposed && _recording);
            return _frames[_frameIndex].RootData;
        }
    }

    internal unsafe GPURange AllocateRootRange<TRoot>(in TRoot value) where TRoot : unmanaged
    {
        GPUAllocation<byte> allocation = AllocateFrameBytes((ulong)sizeof(TRoot), out _, out _);
        *(TRoot*)allocation.Cpu = value;
        return allocation.GPURange;
    }

    /// <summary>Allocates and writes one frame-scoped root struct into auto-paging transient storage.</summary>
    /// <typeparam name="TRoot">The unmanaged root struct type.</typeparam>
    /// <param name="value">The root payload to copy into per-frame storage.</param>
    /// <returns>The 16-byte-aligned <see cref="BufferSlice"/> covering the written root data.</returns>
    public unsafe BufferSlice AllocateRoot<TRoot>(in TRoot value) where TRoot : unmanaged
    {
        ulong byteCount = (ulong)sizeof(TRoot);
        GPUAllocation<byte> allocation = AllocateFrameBytes(byteCount, out Buffer owner, out ulong offset);
        *(TRoot*)allocation.Cpu = value;
        return new BufferSlice(owner, offset, byteCount);
    }

    /// <summary>Uploads transient per-frame data into CPU-visible frame arena storage (auto-paging on overflow).</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="data">The elements to copy into transient GPU-readable storage.</param>
    /// <returns>A slice valid for the current recorded frame, or empty when <paramref name="data"/> is empty.</returns>
    public unsafe BufferSlice UploadTransient<T>(ReadOnlySpan<T> data) where T : unmanaged
    {
        if (data.IsEmpty)
            return default;
        ulong byteCount = (ulong)data.Length * (ulong)sizeof(T);
        GPUAllocation<byte> allocation = AllocateFrameBytes(byteCount, out Buffer owner, out ulong offset);
        fixed (T* source = data)
            NativeMemory.Copy(source, allocation.Cpu, (nuint)byteCount);
        return new BufferSlice(owner, offset, byteCount);
    }

    /// <summary>Uploads a single transient struct into CPU-visible frame arena storage (auto-paging on overflow).</summary>
    /// <typeparam name="T">The unmanaged struct type.</typeparam>
    /// <param name="value">The value to copy into transient GPU-readable storage.</param>
    /// <returns>A slice covering the uploaded element for the current recorded frame.</returns>
    public BufferSlice UploadTransient<T>(in T value) where T : unmanaged =>
        UploadTransient(MemoryMarshal.CreateReadOnlySpan(in Unsafe.AsRef(in value), 1));

    /// <summary>Reserves transient frame arena storage and returns a writable span plus its GPU slice.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="count">Number of elements to reserve.</param>
    /// <param name="slice">Receives the GPU buffer slice valid for the current recorded frame.</param>
    /// <returns>A writable CPU span covering <paramref name="count"/> elements.</returns>
    public unsafe Span<T> AllocateTransient<T>(int count, out BufferSlice slice) where T : unmanaged
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            slice = default;
            return Span<T>.Empty;
        }
        ulong byteCount = (ulong)count * (ulong)sizeof(T);
        GPUAllocation<byte> allocation = AllocateFrameBytes(byteCount, out Buffer owner, out ulong offset);
        slice = new BufferSlice(owner, offset, byteCount);
        return new Span<T>(allocation.Cpu, count);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private GPUAllocation<byte> AllocateFrameBytes(ulong byteCount, out Buffer ownerBuffer, out ulong relativeOffset)
    {
        Debug.Assert(!IsDisposed && _recording && byteCount != 0);
        FrameSlot frame = _frames[_frameIndex];
        GPUAllocation<byte> allocation = frame.RootData.Allocate(byteCount);
        if (allocation.SizeBytes != 0)
        {
            ownerBuffer = frame.MainBuffer;
            relativeOffset = allocation.Offset - frame.MainBaseOffset;
            return allocation;
        }
        return AllocateOverflowBytes(frame, byteCount, out ownerBuffer, out relativeOffset);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private GPUAllocation<byte> AllocateOverflowBytes(FrameSlot frame, ulong byteCount, out Buffer ownerBuffer, out ulong relativeOffset)
    {
        List<OverflowPage>? pages = frame.OverflowPages;
        if (pages != null)
        {
            for (int index = 0; index < pages.Count; ++index)
            {
                OverflowPage page = pages[index];
                GPUAllocation<byte> existing = page.Allocator.Allocate(byteCount);
                if (existing.SizeBytes != 0)
                {
                    ownerBuffer = page.Buffer;
                    relativeOffset = existing.Offset - page.BaseOffset;
                    return existing;
                }
            }
        }

        ulong alignedRequest = (byteCount + BumpAllocator.Alignment - 1) & ~(BumpAllocator.Alignment - 1);
        ulong previousCapacity = pages != null && pages.Count > 0 ? pages[^1].CapacityBytes : _frameDataByteCount;
        ulong doubled = previousCapacity <= ulong.MaxValue / 2 ? previousCapacity * 2 : ulong.MaxValue;
        ulong pageCapacity = Math.Max(alignedRequest, doubled);
        GPUHeap heap = _system.Device.CreateGPUHeap(pageCapacity, MemoryType.CpuVisible);
        try
        {
            GPUAllocation<byte> storage = heap.Range;
            Buffer pageBuffer = new(_system, storage, MemoryType.CpuVisible);
            BumpAllocator allocator = new(storage);
            GPUAllocation<byte> allocation = allocator.Allocate(byteCount);
            pages ??= [];
            frame.OverflowPages = pages;
            pages.Add(new OverflowPage
            {
                Heap = heap,
                Allocator = allocator,
                Buffer = pageBuffer,
                CapacityBytes = pageCapacity,
                BaseOffset = storage.Offset,
            });
            ownerBuffer = pageBuffer;
            relativeOffset = allocation.Offset - storage.Offset;
            return allocation;
        }
        catch
        {
            heap.Destroy();
            throw;
        }
    }

    /// <summary>Waits for the next slot, collects its timestamps, and begins recording; resolve the previous frame before calling.</summary>
    /// <exception cref="GPUException">Command recording could not begin.</exception>
    public void BeginFrame()
    {
        Debug.Assert(!IsDisposed && _commands == null);
        _system.Tick();
        FrameSlot frame = _frames[_frameIndex];
        Reclaim(frame);
        frame.Pool.Reset();
        frame.RootData.Reset();
        if (frame.OverflowPages != null)
        {
            for (int index = 0; index < frame.OverflowPages.Count; ++index)
                frame.OverflowPages[index].Allocator.Reset();
        }
        frame.UploadData?.Reset();
        _commands = frame.Pool.BeginCommands();
        _recording = true;
        _recordingId = _system.OnBeginRecording();
        frame.FrameId = ++_nextFrameId;
        _activeFrameId = frame.FrameId;
        _recordedTimestampCount = 0;
    }

    /// <summary>Ends and submits the active frame without presenting or waiting for GPU completion. Close all scopes first.</summary>
    /// <exception cref="GPUException">Initialization command recording could not begin.</exception>
    public void Submit()
    {
        _system.Submit([this]);
    }

    internal TimelinePoint SubmitInternal(scoped ReadOnlySpan<TimelinePoint> waits = default)
    {
        return _system.SubmitInternal([this], waits);
    }

    internal TimelinePoint SubmitAndPresentInternal(GPUSwapchain swapchain, scoped ReadOnlySpan<TimelinePoint> waits = default)
    {
        Debug.Assert(swapchain != null);
        return _system.SubmitPrepared([this], swapchain, waits, _system.PrepareSubmission());
    }

    internal GPUCommandBuffer EndFrameInternal()
    {
        GPUCommandBuffer commands = EndRecording();
        _system.FlushUploads();
        return commands;
    }

    internal void MarkSubmittedInternal(TimelinePoint completion)
    {
        Debug.Assert(!IsDisposed && !_recording && _commands != null && completion.Semaphore != null);
        RegisterSubmission(completion, _system.OnSubmitted(completion, DetachActiveRecording()));
    }

    internal GPUCommandBuffer EndRecording()
    {
        GPUCommandBuffer commands = CommandsBetweenScopes;
        commands.EndCommands();
        _recording = false;
        _system.OnEndRecording(_recordingId);
        return commands;
    }

    internal void RegisterSubmission(TimelinePoint completion, ulong submissionValue)
    {
        FrameSlot frame = _frames[_frameIndex];
        frame.Completion = completion;
        frame.SubmissionValue = submissionValue;
        _commands = null;
        _activeFrameId = 0;
        _recordedTimestampCount = 0;
        _frameIndex = (_frameIndex + 1) % _frames.Length;
    }

    /// <summary>Discards an unsubmitted frame and invalidates its scopes, commands, and data; swapchain cancellation remains caller-owned.</summary>
    public void DiscardFrame()
    {
        Debug.Assert(!IsDisposed);
        if (_commands == null)
            return;
        if (HasRenderPass)
            EndRenderPass();
        if (_recording)
            _commands.EndCommands();
        FrameSlot frame = _frames[_frameIndex];
        frame.Pool.Reset();
        frame.RootData.Reset();
        if (frame.OverflowPages != null)
        {
            for (int index = 0; index < frame.OverflowPages.Count; ++index)
                frame.OverflowPages[index].Allocator.Reset();
        }
        frame.UploadData?.Reset();
        frame.FrameId = 0;
        _system.OnDiscardRecording(DetachActiveRecording());
        _commands = null;
        _recording = false;
        _activeFrameId = 0;
        _recordedTimestampCount = 0;
        ++_discardVersion;
        ResetScope();
    }

    /// <summary>Waits for submitted slots and collects timestamps without submitting the current frame.</summary>
    public void Wait()
    {
        Debug.Assert(!IsDisposed);
        foreach (FrameSlot frame in _frames)
            Reclaim(frame);
    }

    /// <summary>Waits for submitted work if needed and releases the context's command pools and frame storage immediately.</summary>
    public override void Dispose() => DestroyImmediate();

    /// <summary>Destroys the command pools and root storage after waiting idle; safe at any point once no frame is being recorded.</summary>
    protected override void Release()
    {
        // A partially constructed context (constructor failure) owns nothing: its cleanup already rolled back.
        if (_frames == null)
            return;
        // A reclaimed context may have unknown in-flight state; waiting idle makes its completions observable before teardown.
        _system.Device.WaitIdle();
        _system.OnDiscardRecording(DetachActiveRecording());
        if (_commands != null)
            ++_discardVersion;
        _commands = null;
        _recording = false;
        _activeFrameId = 0;
        _recordedTimestampCount = 0;
        ResetScope();
        foreach (FrameSlot frame in _frames)
        {
            _system.OnCompleted(frame.SubmissionValue);
            frame.SubmissionValue = 0;
            frame.Completion = default;
            frame.FrameId = 0;
            frame.Pool.Destroy();
            frame.UploadHeap.Destroy();
            if (frame.OverflowPages != null)
            {
                for (int index = 0; index < frame.OverflowPages.Count; ++index)
                    frame.OverflowPages[index].Heap.Destroy();
                frame.OverflowPages.Clear();
            }
        }
        _rootHeap.Destroy();
    }

    internal SwapchainFrame AcquireInternal(GPUSwapchain swapchain)
    {
        return CommandsBetweenScopes.Acquire(swapchain);
    }

    /// <summary>Records an execution and memory dependency between scopes.</summary>
    /// <param name="before">The source stages.</param>
    /// <param name="beforeAccess">The source accesses.</param>
    /// <param name="after">The destination stages.</param>
    /// <param name="afterAccess">The destination accesses.</param>
    public void Barrier(Stage before, Access beforeAccess, Stage after, Access afterAccess)
    {
        CommandsBetweenScopes.Barrier(before, beforeAccess, after, afterAccess);
    }

    /// <summary>Records a timestamp between scopes, written when the frame slot's timestamps are collected.</summary>
    /// <param name="destination">Stationary CPU storage with at least one element, kept alive until the timestamp is collected.</param>
    /// <param name="stage">The pipeline stage to timestamp.</param>
    public void WriteTimestamp(Span<ulong> destination, Stage stage = Stage.AllCommands)
    {
        CommandsBetweenScopes.WriteTimestamp(destination, stage);
        ++_recordedTimestampCount;
    }

    /// <summary>Records a buffer-to-buffer copy between scopes without inserting a barrier.</summary>
    /// <param name="source">The source slice, kept live or disposed-deferred until GPU completion.</param>
    /// <param name="destination">The destination slice, large enough to receive <paramref name="source"/>.</param>
    public void CopyBuffer(BufferSlice source, BufferSlice destination)
    {
        if (source.IsEmpty)
            return;
        Debug.Assert(source.Buffer != null && !source.Buffer.IsDisposed && source.Buffer.System == _system);
        Debug.Assert(destination.Buffer != null && !destination.Buffer.IsDisposed && destination.Buffer.System == _system);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(source.SizeBytes, destination.SizeBytes);
        CommandsBetweenScopes.CopyMemory(source.LowLevelRange, destination.LowLevelRange.Slice(0, source.SizeBytes));
    }

    /// <summary>Records a buffer-to-buffer copy between scopes without inserting a barrier.</summary>
    /// <param name="source">The source buffer.</param>
    /// <param name="sourceOffset">The source byte offset.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <param name="destinationOffset">The destination byte offset.</param>
    /// <param name="byteCount">The number of bytes to copy.</param>
    public void CopyBuffer(Buffer source, ulong sourceOffset, Buffer destination, ulong destinationOffset, ulong byteCount)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        CopyBuffer(source.Slice(sourceOffset, byteCount), destination.Slice(destinationOffset, byteCount));
    }

    /// <summary>Records a buffer-to-texture copy between scopes without inserting a barrier.</summary>
    /// <param name="source">The source buffer slice.</param>
    /// <param name="destination">The single-sample destination texture.</param>
    /// <param name="copy">The destination texture region and source memory pitches.</param>
    public void CopyBufferToTexture(BufferSlice source, Texture destination, TextureCopyDesc copy = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Debug.Assert(source.Buffer != null && !source.Buffer.IsDisposed && source.Buffer.System == _system);
        Debug.Assert(!destination.IsDisposed && destination.System == _system);
        CommandsBetweenScopes.CopyMemoryToTexture(source.LowLevelRange, destination.LowLevelTexture, copy);
    }

    /// <summary>Records a texture-to-buffer copy between scopes without inserting a barrier.</summary>
    /// <param name="source">The single-sample source texture.</param>
    /// <param name="destination">The destination buffer slice.</param>
    /// <param name="copy">The texture region and destination pitches.</param>
    public void CopyTextureToBuffer(Texture source, BufferSlice destination, TextureCopyDesc copy = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        Debug.Assert(!source.IsDisposed && source.System == _system);
        Debug.Assert(destination.Buffer != null && !destination.Buffer.IsDisposed && destination.Buffer.System == _system);
        CommandsBetweenScopes.CopyTextureToMemory(source.LowLevelTexture, destination.LowLevelRange, copy);
    }

    /// <summary>Records a non-overlapping texture-to-texture copy between scopes without inserting a barrier.</summary>
    /// <param name="source">The single-sample source texture.</param>
    /// <param name="destination">The single-sample destination texture.</param>
    /// <param name="copy">The source and destination regions.</param>
    public void CopyTexture(Texture source, Texture destination, TextureRegionCopyDesc copy = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        Debug.Assert(!source.IsDisposed && source.System == _system);
        Debug.Assert(!destination.IsDisposed && destination.System == _system);
        CommandsBetweenScopes.CopyTexture(source.LowLevelTexture, destination.LowLevelTexture, copy);
    }

    internal void CopyMemoryInternal(GPURange source, GPURange destination)
    {
        CommandsBetweenScopes.CopyMemory(source, destination);
    }

    internal void CopyMemoryToTextureInternal(GPURange source, GPUTexture destination, TextureCopyDesc copy = default)
    {
        CommandsBetweenScopes.CopyMemoryToTexture(source, destination, copy);
    }

    internal void CopyTextureToMemoryInternal(GPUTexture source, GPURange destination, TextureCopyDesc copy = default)
    {
        CommandsBetweenScopes.CopyTextureToMemory(source, destination, copy);
    }

    internal void CopyTextureInternal(GPUTexture source, GPUTexture destination, TextureRegionCopyDesc copy = default)
    {
        CommandsBetweenScopes.CopyTexture(source, destination, copy);
    }

    /// <summary>Opens a named debug group on the recording command buffer until the matching <see cref="EndDebugGroup"/>.</summary>
    /// <param name="name">The group name shown by GPU capture tools.</param>
    public void BeginDebugGroup(ReadOnlySpan<char> name)
    {
        RecordingCommands.BeginDebugGroup(name);
    }

    /// <summary>Closes the innermost open debug group on the recording command buffer.</summary>
    public void EndDebugGroup()
    {
        RecordingCommands.EndDebugGroup();
    }

    /// <summary>Inserts a standalone debug marker on the recording command buffer.</summary>
    /// <param name="name">The marker name shown by GPU capture tools.</param>
    public void InsertDebugMarker(ReadOnlySpan<char> name)
    {
        RecordingCommands.InsertDebugMarker(name);
    }

    /// <summary>Copies content into frame-owned staging and records its upload between scopes.</summary>
    /// <param name="texture">The destination from this context's system.</param>
    /// <param name="source">The region data including pitch padding, copied before this method returns.</param>
    /// <param name="copy">The destination mip, slices, region, and memory pitches; the default writes mip zero.</param>
    /// <exception cref="InvalidOperationException">The recording frame's remaining upload capacity is insufficient.</exception>
    /// <exception cref="GPUException">Frame upload storage could not be created.</exception>
    public unsafe void SetTextureData(Texture texture, ReadOnlySpan<byte> source, in TextureCopyDesc copy = default)
    {
        GPUCommandBuffer commands = CommandsBetweenScopes;
        Debug.Assert(texture != null && !texture.IsDisposed && texture.System == _system && !source.IsEmpty);
        ulong byteCount = TextureUpload.GetSizeBytes(texture.Desc, copy);
        if (byteCount > _uploadByteCount)
            throw new InvalidOperationException("The texture upload exceeds this context's per-frame upload capacity.");
        FrameSlot frame = _frames[_frameIndex];
        if (frame.UploadData == null)
        {
            GPUHeap heap = _system.Device.CreateGPUHeap(_uploadByteCount, MemoryType.CpuVisible);
            GPUAllocation<byte> storage = heap.Range;
            storage.SizeBytes = _uploadByteCount;
            frame.UploadData = new BumpAllocator(storage);
            frame.UploadHeap = heap;
        }
        GPUAllocation<byte> staging = frame.UploadData.Allocate(byteCount);
        if (staging.SizeBytes == 0)
            throw new InvalidOperationException("The recording frame's upload storage is exhausted.");
        commands.Barrier(Stage.AllCommands, TextureAccess, Stage.Transfer, Access.TransferRead | Access.TransferWrite);
        TextureUpload.UploadTexture(commands, staging, texture.LowLevelTexture, texture.Desc, source, copy);
        commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.AllCommands, TextureAccess);
    }

    /// <summary>Begins a render pass within the active frame, deriving attachment formats and sample count from <paramref name="desc"/>.</summary>
    /// <param name="desc">The attachments and their operations.</param>
    /// <returns>The borrowed scope, valid until the pass ends or the frame is discarded.</returns>
    public RenderScope BeginRenderPass(scoped RenderingDesc desc)
    {
        GPUColorAttachmentArray colors = default;
        GPURenderingDesc nativeDesc = ToLowLevel(in desc, ref colors);
        return BeginRenderPassInternal(nativeDesc, RenderPassSignature.From(in nativeDesc));
    }

    /// <summary>Begins a render pass within the active frame using an explicit pass signature.</summary>
    /// <param name="desc">The attachments and their operations.</param>
    /// <param name="signature">The pass attachment formats and sample count.</param>
    /// <returns>The borrowed scope, valid until the pass ends or the frame is discarded.</returns>
    public RenderScope BeginRenderPass(scoped RenderingDesc desc, RenderPassSignature signature)
    {
        GPUColorAttachmentArray colors = default;
        GPURenderingDesc nativeDesc = ToLowLevel(in desc, ref colors);
        return BeginRenderPassInternal(nativeDesc, signature);
    }

    /// <summary>Begins a render pass within the active frame. No other scope may be open.</summary>
    /// <param name="desc">The attachments and their operations.</param>
    /// <param name="colorFormats">The formats matching the color attachments in order.</param>
    /// <param name="depthFormat">The depth attachment format, or Undefined when absent.</param>
    /// <param name="stencilFormat">The stencil attachment format, or Undefined when absent.</param>
    /// <returns>The borrowed scope, valid until the pass ends or the frame is discarded.</returns>
    public RenderScope BeginRenderPass(scoped RenderingDesc desc, scoped ReadOnlySpan<Format> colorFormats,
        Format depthFormat = Format.Undefined, Format stencilFormat = Format.Undefined)
    {
        GPUColorAttachmentArray colors = default;
        GPURenderingDesc nativeDesc = ToLowLevel(in desc, ref colors);
        return BeginRenderPassInternal(nativeDesc, colorFormats, depthFormat, stencilFormat);
    }

    internal RenderScope BeginRenderPassInternal(scoped GPURenderingDesc desc)
    {
        return BeginRenderPassInternal(desc, RenderPassSignature.From(in desc));
    }

    internal RenderScope BeginRenderPassInternal(scoped GPURenderingDesc desc, RenderPassSignature signature)
    {
        GPUCommandBuffer commands = CommandsBetweenScopes;
        Debug.Assert(signature.ColorCount == desc.Colors.Length);
        _passSignature = signature;

        commands.SetTextureDescriptorHeap(_system.TextureDescriptorHeap);
        commands.SetSamplerDescriptorHeap(_system.SamplerDescriptorHeap);
        commands.BeginRenderPass(desc);
        _scope = ScopeKind.Render;
        return new RenderScope(this, ++_scopeId);
    }

    internal RenderScope BeginRenderPassInternal(scoped GPURenderingDesc desc, scoped ReadOnlySpan<Format> colorFormats,
        Format depthFormat = Format.Undefined, Format stencilFormat = Format.Undefined)
    {
        Debug.Assert(colorFormats.Length == desc.Colors.Length);
        uint sampleCount = DeriveSampleCount(in desc);
        return BeginRenderPassInternal(desc, new RenderPassSignature(colorFormats, depthFormat, stencilFormat, sampleCount));
    }

    private static GPURenderingDesc ToLowLevel(scoped in RenderingDesc desc, ref GPUColorAttachmentArray colorsStorage)
    {
        int colorCount = desc.Colors.Length;
        Debug.Assert(colorCount <= (int)Backend.MaxColorAttachments);
        Span<GPUColorAttachment> colors = colorsStorage;
        for (int index = 0; index < colorCount; ++index)
        {
            ColorAttachment src = desc.Colors[index];
            colors[index] = new GPUColorAttachment
            {
                RenderView = src.Target.View,
                ResolveView = src.ResolveTarget.View,
                Load = src.Load,
                Store = src.Store,
                Clear = src.Clear,
            };
        }
        return new GPURenderingDesc
        {
            Colors = colors[..colorCount],
            Depth = new GPUDepthAttachment
            {
                RenderView = desc.Depth.Target.View,
                Load = desc.Depth.Load,
                Store = desc.Depth.Store,
                Clear = desc.Depth.Clear,
            },
            Stencil = new GPUStencilAttachment
            {
                RenderView = desc.Stencil.Target.View,
                Load = desc.Stencil.Load,
                Store = desc.Stencil.Store,
                Clear = desc.Stencil.Clear,
            },
        };
    }

    private static uint DeriveSampleCount(scoped in GPURenderingDesc desc)
    {
        for (int index = 0; index < desc.Colors.Length; ++index)
        {
            GPURenderView? view = desc.Colors[index].RenderView;
            if (view != null)
                return view.SampleCount;
        }
        if (desc.Depth.RenderView != null)
            return desc.Depth.RenderView.SampleCount;
        if (desc.Stencil.RenderView != null)
            return desc.Stencil.RenderView.SampleCount;
        return 1;
    }

    internal RenderScope BeginPreparedRenderPassInternal(scoped GPURenderingDesc desc, GPUPipeline pipeline,
        GPUTextureDescriptorHeap textures, GPUSamplerDescriptorHeap samplers)
    {
        GPUCommandBuffer commands = CommandsBetweenScopes;
        commands.SetTextureDescriptorHeap(textures);
        commands.SetSamplerDescriptorHeap(samplers);
        RenderScope render = BeginPreparedRenderPassInternal(desc);
        commands.BindPipeline(pipeline);
        commands.SetDepthStencil(default);
        return render;
    }

    internal RenderScope BeginPreparedRenderPassInternal(scoped GPURenderingDesc desc)
    {
        CommandsBetweenScopes.BeginRenderPass(desc);
        _scope = ScopeKind.PreparedRender;
        return new RenderScope(this, ++_scopeId);
    }

    private void EndRenderPass()
    {
        Debug.Assert(HasRenderPass);
        RecordingCommands.EndRenderPass();
        ResetScope();
    }

    /// <summary>Begins compute recording in the active frame. No other scope may be open.</summary>
    /// <returns>The borrowed scope, valid until it ends or the frame is discarded.</returns>
    public ComputeScope BeginCompute()
    {
        GPUCommandBuffer commands = CommandsBetweenScopes;
        commands.SetTextureDescriptorHeap(_system.TextureDescriptorHeap);
        commands.SetSamplerDescriptorHeap(_system.SamplerDescriptorHeap);
        _scope = ScopeKind.Compute;
        return new ComputeScope(this, ++_scopeId);
    }

    internal void ApplyDrawState(GPUPipeline pipeline, DepthStencilState depthStencil)
    {
        Debug.Assert(_commands != null);
        if (_lastPipeline != pipeline)
        {
            _commands.BindPipeline(pipeline);
            _lastPipeline = pipeline;
        }
        if (!HasDepthStencil || LastDepthStencil != depthStencil)
        {
            _commands.SetDepthStencil(depthStencil);
            LastDepthStencil = depthStencil;
            HasDepthStencil = true;
        }
    }

    private void Reclaim(FrameSlot frame)
    {
        if (frame.Completion.Semaphore == null)
            return;
        frame.Completion.Wait();
        frame.Pool.ReadTimestamps();
        frame.FrameId = 0;
        _system.OnCompleted(frame.SubmissionValue);
        frame.SubmissionValue = 0;
        frame.Completion = default;
    }

    private void ResetScope()
    {
        _scope = ScopeKind.None;
        _passSignature = default;
        _lastVariant = null;
        _lastPipeline = null;
        HasDepthStencil = false;
    }

    private bool IsScopeActive(ScopeKind kind, uint id) => _scope == kind && _scopeId == id;

    private bool IsRenderScopeActive(uint id) => HasRenderPass && _scopeId == id;

    [Conditional("DEBUG")]
    private void AssertScope(ScopeKind kind, uint id)
    {
        Debug.Assert(!IsDisposed && _recording && IsScopeActive(kind, id));
    }

    private GPURange PrepareDraw(Material material, ShaderKind kind = ShaderKind.Graphics)
    {
        BindMaterial(material, material.DepthStencilState, kind);
        return AllocateMaterialRoot(material);
    }

    private GPURange PrepareDrawWithPass<TPass>(Material material, RootField<TPass> passField, in TPass pass,
        DepthStencilState depthStencil, ShaderKind kind = ShaderKind.Graphics) where TPass : unmanaged
    {
        BindMaterial(material, depthStencil, kind);
        return AllocateMaterialRootWithOverride(material, passField, in pass);
    }

    private GPURange AllocateMaterialRoot(Material material)
    {
        uint rootSize = material.Root.SizeBytes;
        Debug.Assert(rootSize != 0);
        GPUAllocation<byte> root = AllocateFrameBytes(rootSize, out _, out _);
        material.CopyRoot(root.CpuSpan);
        return root.GPURange;
    }

    private GPURange AllocateMaterialRootWithOverride<TPass>(Material material, RootField<TPass> passField, in TPass pass)
        where TPass : unmanaged
    {
        uint rootSize = material.Root.SizeBytes;
        Debug.Assert(rootSize != 0 && passField.IsValid && passField.Offset + passField.SizeBytes <= rootSize);
        GPUAllocation<byte> root = AllocateFrameBytes(rootSize, out _, out _);
        material.CopyRoot(root.CpuSpan);
        MemoryMarshal.Write(root.CpuSpan.Slice((int)passField.Offset, (int)passField.SizeBytes), in pass);
        return root.GPURange;
    }

    private unsafe GPURange AllocateRawRoot<TRoot>(in TRoot value) where TRoot : unmanaged
    {
        GPUAllocation<byte> root = AllocateFrameBytes((ulong)sizeof(TRoot), out _, out _);
        *(TRoot*)root.Cpu = value;
        return root.GPURange;
    }

    private void BindMaterial(Material material, DepthStencilState depthStencil, ShaderKind kind = ShaderKind.Graphics)
    {
        Debug.Assert(_recording && _scope == ScopeKind.Render && _commands != null && material.Shader.System == _system);
        Shader shader = material.Shader;
        Debug.Assert(!shader.IsDisposed && shader.Kind == kind);
        ShaderVariant variant = material.Variant;
        PipelineStateKey stateKey = material.StateKey;
        GPUPipeline? pipeline = _lastPipeline;
        if (pipeline == null || _lastVariant != variant || !_lastStateKey.Equals(stateKey))
        {
            pipeline = ResolveDrawPipeline(variant, material.State, in stateKey);
            _lastVariant = variant;
            _lastStateKey = stateKey;
        }
        ApplyDrawState(pipeline, depthStencil);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private GPUPipeline ResolveDrawPipeline(ShaderVariant variant, in PipelineState state, in PipelineStateKey stateKey)
    {
        return variant.TryGetCachedPipeline(in _passSignature, in stateKey) ??
            ResolveUncachedPipeline(variant, in state, in stateKey);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private GPUPipeline ResolveUncachedPipeline(ShaderVariant variant, in PipelineState state, in PipelineStateKey stateKey)
    {
        PipelineMissPolicy policy = _system.PipelineMissPolicy;
        if (policy == PipelineMissPolicy.Throw)
        {
            throw new InvalidOperationException(
                $"Shader '{variant.OwnerName}' needs a prepared pipeline; call Material.GetPipeline or WarmUpPipeline for this state and pass.");
        }

        GPUPipeline pipeline = variant.GetPipeline(in state, in stateKey, in _passSignature, out bool created);
        if (created && policy == PipelineMissPolicy.CreateAndWarn)
        {
            Gpu.ReportError(
                $"Pipeline miss: compiled '{variant.OwnerName}' pipeline on draw (Colors={_passSignature.ColorCount}, Depth={_passSignature.DepthFormat}, Stencil={_passSignature.StencilFormat}, Samples={_passSignature.SampleCount}).",
                false);
        }
        return pipeline;
    }

    private GPURange PrepareDispatch(Material material)
    {
        BindCompute(material.Shader, material);
        return AllocateMaterialRoot(material);
    }

    private GPURange PrepareDispatchWithPass<TPass>(Material material, RootField<TPass> passField, in TPass pass) where TPass : unmanaged
    {
        BindCompute(material.Shader, material);
        return AllocateMaterialRootWithOverride(material, passField, in pass);
    }

    private void BindCompute(Shader shader, Material? material)
    {
        Debug.Assert(_recording && _scope == ScopeKind.Compute && _commands != null && !shader.IsDisposed && shader.Kind == ShaderKind.Compute && shader.System == _system);
        ShaderVariant variant = material != null ? material.Variant : shader.DefaultVariant;
        if (_lastVariant != variant)
        {
            _commands.BindPipeline(variant.GetComputePipeline());
            _lastVariant = variant;
        }
    }

    private GPURange ValidateRootSlice(BufferSlice root, uint minSize)
    {
        Debug.Assert(root.Buffer != null && !root.Buffer.IsDisposed && root.Buffer.System == _system);
        GPURange range = root.LowLevelRange;
        Debug.Assert(range.Buffer != null && range.Offset % BumpAllocator.Alignment == 0 && range.Size >= minSize);
        return range;
    }

    private GPURange ValidateBufferSlice(BufferSlice slice)
    {
        Debug.Assert(slice.Buffer != null && !slice.Buffer.IsDisposed && slice.Buffer.System == _system);
        return slice.LowLevelRange;
    }

    /// <summary>Records graphics and mesh commands in one active render pass.</summary>
    public readonly ref struct RenderScope
    {
        private readonly RenderContext _context;
        private readonly uint _id;

        internal RenderScope(RenderContext context, uint id)
        {
            _context = context;
            _id = id;
        }

        internal GPUCommandBuffer PreparedCommands
        {
            get
            {
                _context.AssertScope(ScopeKind.PreparedRender, _id);
                return _context.RecordingCommands;
            }
        }

        /// <summary>Sets the viewport for subsequent draws.</summary>
        /// <param name="viewport">The viewport to use.</param>
        public void SetViewport(Viewport viewport)
        {
            Debug.Assert(_context.IsRenderScopeActive(_id));
            _context.RecordingCommands.SetViewport(viewport);
        }

        /// <summary>Sets the scissor for subsequent draws.</summary>
        /// <param name="scissor">The scissor rectangle.</param>
        public void SetScissor(Scissor scissor)
        {
            Debug.Assert(_context.IsRenderScopeActive(_id));
            _context.RecordingCommands.SetScissor(scissor);
        }

        internal void DrawLowLevel(GPURange root, uint vertexCount)
        {
            PreparedCommands.Draw(root, vertexCount);
        }

        /// <summary>Records a draw with a fresh root snapshot.</summary>
        public void Draw(Material material, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange root = _context.PrepareDraw(material);
            _context.RecordingCommands.Draw(root, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        /// <summary>Records a draw overriding <paramref name="passField"/> in the material's root snapshot.</summary>
        public void Draw<TPass>(Material material, RootField<TPass> passField, in TPass pass, uint vertexCount,
            uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) where TPass : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange root = _context.PrepareDrawWithPass(material, passField, in pass, material.DepthStencilState);
            _context.RecordingCommands.Draw(root, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        /// <summary>Records a draw overriding <paramref name="passField"/> and depth-stencil state.</summary>
        public void Draw<TPass>(Material material, RootField<TPass> passField, in TPass pass, DepthStencilState depthStencil,
            uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) where TPass : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange root = _context.PrepareDrawWithPass(material, passField, in pass, depthStencil);
            _context.RecordingCommands.Draw(root, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        /// <summary>Records a draw with an explicit typed root struct and draw-local depth-stencil state.</summary>
        public unsafe void Draw<TRoot>(Material material, in TRoot root, DepthStencilState depthStencil, uint vertexCount,
            uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0) where TRoot : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert((uint)sizeof(TRoot) >= material.Root.SizeBytes);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.Draw(_context.AllocateRawRoot(in root), vertexCount, instanceCount, firstVertex, firstInstance);
        }

        /// <summary>Records a draw with a prepacked root <see cref="BufferSlice"/> and draw-local depth-stencil state.</summary>
        public void Draw(Material material, BufferSlice root, DepthStencilState depthStencil, uint vertexCount,
            uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange range = _context.ValidateRootSlice(root, material.Root.SizeBytes);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.Draw(range, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        internal void DrawLowLevel(Material material, GPURange root, DepthStencilState depthStencil, uint vertexCount,
            uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(root.Buffer != null && root.Offset % BumpAllocator.Alignment == 0 && root.Size >= material.Root.SizeBytes);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.Draw(root, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        /// <summary>Records an indexed draw with a fresh root snapshot.</summary>
        public void DrawIndexed(Material material, Buffer indices, IndexType type, uint indexCount, uint instanceCount = 1,
            uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0, ulong byteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(indices != null && !indices.IsDisposed && indices.System == _context._system && byteOffset < indices.SizeBytes);
            GPURange root = _context.PrepareDraw(material);
            _context.RecordingCommands.DrawIndexed(root, indices.LowLevelRange.Slice(byteOffset), type, indexCount, instanceCount, firstIndex, vertexOffset,
                firstInstance);
        }

        /// <summary>Records an indexed draw using a bounded index <see cref="BufferSlice"/>.</summary>
        public void DrawIndexed(Material material, BufferSlice indices, IndexType type, uint indexCount, uint instanceCount = 1,
            uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange indexRange = _context.ValidateBufferSlice(indices);
            GPURange root = _context.PrepareDraw(material);
            _context.RecordingCommands.DrawIndexed(root, indexRange, type, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        /// <summary>Records an indexed draw overriding <paramref name="passField"/> in the material's root snapshot.</summary>
        public void DrawIndexed<TPass>(Material material, RootField<TPass> passField, in TPass pass,
            BufferSlice indices, IndexType type, uint indexCount, uint instanceCount = 1,
            uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0) where TPass : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange indexRange = _context.ValidateBufferSlice(indices);
            GPURange root = _context.PrepareDrawWithPass(material, passField, in pass, material.DepthStencilState);
            _context.RecordingCommands.DrawIndexed(root, indexRange, type, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        /// <summary>Records an indexed draw overriding <paramref name="passField"/> and depth-stencil state.</summary>
        public void DrawIndexed<TPass>(Material material, RootField<TPass> passField, in TPass pass, DepthStencilState depthStencil,
            BufferSlice indices, IndexType type, uint indexCount, uint instanceCount = 1,
            uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0) where TPass : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange indexRange = _context.ValidateBufferSlice(indices);
            GPURange root = _context.PrepareDrawWithPass(material, passField, in pass, depthStencil);
            _context.RecordingCommands.DrawIndexed(root, indexRange, type, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        /// <summary>Records an indexed draw with an explicit typed root struct and draw-local depth-stencil state.</summary>
        public unsafe void DrawIndexed<TRoot>(Material material, in TRoot root, DepthStencilState depthStencil,
            BufferSlice indices, IndexType type, uint indexCount, uint instanceCount = 1,
            uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0) where TRoot : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert((uint)sizeof(TRoot) >= material.Root.SizeBytes);
            GPURange indexRange = _context.ValidateBufferSlice(indices);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.DrawIndexed(_context.AllocateRawRoot(in root), indexRange, type, indexCount, instanceCount, firstIndex,
                vertexOffset, firstInstance);
        }

        /// <summary>Records an indexed draw with a prepacked root <see cref="BufferSlice"/> and draw-local depth-stencil state.</summary>
        public void DrawIndexed(Material material, BufferSlice root, DepthStencilState depthStencil,
            BufferSlice indices, IndexType type, uint indexCount, uint instanceCount = 1,
            uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange rootRange = _context.ValidateRootSlice(root, material.Root.SizeBytes);
            GPURange indexRange = _context.ValidateBufferSlice(indices);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.DrawIndexed(rootRange, indexRange, type, indexCount, instanceCount, firstIndex, vertexOffset, firstInstance);
        }

        internal void DrawIndexedLowLevel(Material material, GPURange root, DepthStencilState depthStencil, Buffer indices, IndexType type,
            uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0, ulong byteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(root.Buffer != null && root.Offset % BumpAllocator.Alignment == 0 && root.Size >= material.Root.SizeBytes);
            Debug.Assert(indices != null && !indices.IsDisposed && indices.System == _context._system && byteOffset < indices.SizeBytes);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.DrawIndexed(root, indices.LowLevelRange.Slice(byteOffset), type, indexCount, instanceCount, firstIndex, vertexOffset,
                firstInstance);
        }

        /// <summary>Records non-indexed indirect draws with a fresh root snapshot.</summary>
        public void DrawIndirect(Material material, Buffer arguments, uint drawCount = 1, uint stride = 0, ulong argumentByteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(arguments != null && !arguments.IsDisposed && arguments.System == _context._system && argumentByteOffset < arguments.SizeBytes);
            GPURange root = _context.PrepareDraw(material);
            _context.RecordingCommands.DrawIndirect(root, arguments.LowLevelRange.Slice(argumentByteOffset), drawCount, stride);
        }

        /// <summary>Records non-indexed indirect draws with a fresh root snapshot and argument slice.</summary>
        public void DrawIndirect(Material material, BufferSlice arguments, uint drawCount = 1, uint stride = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            GPURange root = _context.PrepareDraw(material);
            _context.RecordingCommands.DrawIndirect(root, argRange, drawCount, stride);
        }

        /// <summary>Records non-indexed indirect draws overriding <paramref name="passField"/>.</summary>
        public void DrawIndirect<TPass>(Material material, RootField<TPass> passField, in TPass pass,
            BufferSlice arguments, uint drawCount = 1, uint stride = 0) where TPass : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            GPURange root = _context.PrepareDrawWithPass(material, passField, in pass, material.DepthStencilState);
            _context.RecordingCommands.DrawIndirect(root, argRange, drawCount, stride);
        }

        /// <summary>Records non-indexed indirect draws overriding <paramref name="passField"/> and depth-stencil state.</summary>
        public void DrawIndirect<TPass>(Material material, RootField<TPass> passField, in TPass pass, DepthStencilState depthStencil,
            BufferSlice arguments, uint drawCount = 1, uint stride = 0) where TPass : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            GPURange root = _context.PrepareDrawWithPass(material, passField, in pass, depthStencil);
            _context.RecordingCommands.DrawIndirect(root, argRange, drawCount, stride);
        }

        /// <summary>Records non-indexed indirect draws with an explicit typed root struct and depth-stencil state.</summary>
        public unsafe void DrawIndirect<TRoot>(Material material, in TRoot root, DepthStencilState depthStencil,
            BufferSlice arguments, uint drawCount = 1, uint stride = 0) where TRoot : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert((uint)sizeof(TRoot) >= material.Root.SizeBytes);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.DrawIndirect(_context.AllocateRawRoot(in root), argRange, drawCount, stride);
        }

        /// <summary>Records non-indexed indirect draws with a prepacked root <see cref="BufferSlice"/> and depth-stencil state.</summary>
        public void DrawIndirect(Material material, BufferSlice root, DepthStencilState depthStencil,
            BufferSlice arguments, uint drawCount = 1, uint stride = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange rootRange = _context.ValidateRootSlice(root, material.Root.SizeBytes);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.DrawIndirect(rootRange, argRange, drawCount, stride);
        }

        internal void DrawIndirectLowLevel(Material material, GPURange root, DepthStencilState depthStencil, Buffer arguments,
            uint drawCount = 1, uint stride = 0, ulong argumentByteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(root.Buffer != null && root.Offset % BumpAllocator.Alignment == 0 && root.Size >= material.Root.SizeBytes);
            Debug.Assert(arguments != null && !arguments.IsDisposed && arguments.System == _context._system && argumentByteOffset < arguments.SizeBytes);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.DrawIndirect(root, arguments.LowLevelRange.Slice(argumentByteOffset), drawCount, stride);
        }

        /// <summary>Records indexed indirect draws with a fresh root snapshot.</summary>
        public void DrawIndexedIndirect(Material material, Buffer indices, IndexType type, Buffer arguments,
            uint drawCount = 1, uint stride = 0, ulong argumentByteOffset = 0, ulong indexByteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(indices != null && !indices.IsDisposed && indices.System == _context._system && indexByteOffset < indices.SizeBytes);
            Debug.Assert(arguments != null && !arguments.IsDisposed && arguments.System == _context._system && argumentByteOffset < arguments.SizeBytes);
            GPURange root = _context.PrepareDraw(material);
            _context.RecordingCommands.DrawIndexedIndirect(root, indices.LowLevelRange.Slice(indexByteOffset), type,
                arguments.LowLevelRange.Slice(argumentByteOffset), drawCount, stride);
        }

        /// <summary>Records indexed indirect draws using bounded slices.</summary>
        public void DrawIndexedIndirect(Material material, BufferSlice indices, IndexType type, BufferSlice arguments,
            uint drawCount = 1, uint stride = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange indexRange = _context.ValidateBufferSlice(indices);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            GPURange root = _context.PrepareDraw(material);
            _context.RecordingCommands.DrawIndexedIndirect(root, indexRange, type, argRange, drawCount, stride);
        }

        /// <summary>Records indexed indirect draws overriding <paramref name="passField"/>.</summary>
        public void DrawIndexedIndirect<TPass>(Material material, RootField<TPass> passField, in TPass pass,
            BufferSlice indices, IndexType type, BufferSlice arguments, uint drawCount = 1, uint stride = 0) where TPass : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange indexRange = _context.ValidateBufferSlice(indices);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            GPURange root = _context.PrepareDrawWithPass(material, passField, in pass, material.DepthStencilState);
            _context.RecordingCommands.DrawIndexedIndirect(root, indexRange, type, argRange, drawCount, stride);
        }

        /// <summary>Records indexed indirect draws overriding <paramref name="passField"/> and depth-stencil state.</summary>
        public void DrawIndexedIndirect<TPass>(Material material, RootField<TPass> passField, in TPass pass, DepthStencilState depthStencil,
            BufferSlice indices, IndexType type, BufferSlice arguments, uint drawCount = 1, uint stride = 0) where TPass : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange indexRange = _context.ValidateBufferSlice(indices);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            GPURange root = _context.PrepareDrawWithPass(material, passField, in pass, depthStencil);
            _context.RecordingCommands.DrawIndexedIndirect(root, indexRange, type, argRange, drawCount, stride);
        }

        /// <summary>Records indexed indirect draws with an explicit typed root struct and depth-stencil state.</summary>
        public unsafe void DrawIndexedIndirect<TRoot>(Material material, in TRoot root, DepthStencilState depthStencil,
            BufferSlice indices, IndexType type, BufferSlice arguments, uint drawCount = 1, uint stride = 0) where TRoot : unmanaged
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert((uint)sizeof(TRoot) >= material.Root.SizeBytes);
            GPURange indexRange = _context.ValidateBufferSlice(indices);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.DrawIndexedIndirect(_context.AllocateRawRoot(in root), indexRange, type, argRange, drawCount, stride);
        }

        /// <summary>Records indexed indirect draws with a prepacked root <see cref="BufferSlice"/> and depth-stencil state.</summary>
        public void DrawIndexedIndirect(Material material, BufferSlice root, DepthStencilState depthStencil,
            BufferSlice indices, IndexType type, BufferSlice arguments, uint drawCount = 1, uint stride = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange rootRange = _context.ValidateRootSlice(root, material.Root.SizeBytes);
            GPURange indexRange = _context.ValidateBufferSlice(indices);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.DrawIndexedIndirect(rootRange, indexRange, type, argRange, drawCount, stride);
        }

        internal void DrawIndexedIndirectLowLevel(Material material, GPURange root, DepthStencilState depthStencil,
            Buffer indices, IndexType type, Buffer arguments,
            uint drawCount = 1, uint stride = 0, ulong argumentByteOffset = 0, ulong indexByteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(root.Buffer != null && root.Offset % BumpAllocator.Alignment == 0 && root.Size >= material.Root.SizeBytes);
            Debug.Assert(indices != null && !indices.IsDisposed && indices.System == _context._system && indexByteOffset < indices.SizeBytes);
            Debug.Assert(arguments != null && !arguments.IsDisposed && arguments.System == _context._system && argumentByteOffset < arguments.SizeBytes);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.DrawIndexedIndirect(root, indices.LowLevelRange.Slice(indexByteOffset), type,
                arguments.LowLevelRange.Slice(argumentByteOffset), drawCount, stride);
        }

        /// <summary>Records a mesh draw with a fresh root snapshot.</summary>
        public void DrawMeshlets(Material material, Extent3D groupCount)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            _context.RecordingCommands.DrawMeshlets(_context.PrepareDraw(material, ShaderKind.Mesh), groupCount);
        }

        /// <summary>Records indirect mesh draws with a fresh root snapshot.</summary>
        public void DrawMeshletsIndirect(Material material, Buffer arguments, uint drawCount = 1, uint stride = 0, ulong byteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(!arguments.IsDisposed && arguments.System == _context._system && byteOffset < arguments.SizeBytes);
            GPURange root = _context.PrepareDraw(material, ShaderKind.Mesh);
            _context.RecordingCommands.DrawMeshletsIndirect(root, arguments.LowLevelRange.Slice(byteOffset), drawCount, stride);
        }

        /// <summary>Records indirect mesh draws with a fresh root snapshot and argument slice.</summary>
        public void DrawMeshletsIndirect(Material material, BufferSlice arguments, uint drawCount = 1, uint stride = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            GPURange root = _context.PrepareDraw(material, ShaderKind.Mesh);
            _context.RecordingCommands.DrawMeshletsIndirect(root, argRange, drawCount, stride);
        }

        /// <summary>Replays sealed draws in the current compatible pass, without allocating frame roots.</summary>
        public void ExecuteBundle(RenderBundle bundle)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(bundle.IsCompatible(_context._system, _context._passSignature));
            bundle.Replay(_context, _context.RecordingCommands);
            _context._lastVariant = null;
        }

        /// <summary>Ends this render pass without submitting the frame.</summary>
        public void End()
        {
            Debug.Assert(_context.IsRenderScopeActive(_id));
            _context.EndRenderPass();
        }

        /// <summary>Ends this render pass if still active, without submitting the frame.</summary>
        public void Dispose()
        {
            if (_context != null && _context.IsRenderScopeActive(_id))
                _context.EndRenderPass();
        }
    }

    /// <summary>Records compute work outside a render pass without implicit synchronization.</summary>
    public readonly ref struct ComputeScope
    {
        private readonly RenderContext _context;
        private readonly uint _id;

        internal ComputeScope(RenderContext context, uint id)
        {
            _context = context;
            _id = id;
        }

        /// <summary>Records a dispatch with a fresh root snapshot.</summary>
        public void Dispatch(Material material, Extent3D groupCount)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            _context.RecordingCommands.Dispatch(_context.PrepareDispatch(material), groupCount);
        }

        /// <summary>Records a dispatch overriding <paramref name="passField"/> in the material's root snapshot.</summary>
        public void Dispatch<TPass>(Material material, RootField<TPass> passField, in TPass pass, Extent3D groupCount) where TPass : unmanaged
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            _context.RecordingCommands.Dispatch(_context.PrepareDispatchWithPass(material, passField, in pass), groupCount);
        }

        /// <summary>Records a dispatch with an explicit typed root struct for <paramref name="material"/>'s active specialization.</summary>
        public unsafe void Dispatch<TRoot>(Material material, in TRoot root, Extent3D groupCount) where TRoot : unmanaged
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert((uint)sizeof(TRoot) >= material.Root.SizeBytes);
            _context.BindCompute(material.Shader, material);
            _context.RecordingCommands.Dispatch(_context.AllocateRawRoot(in root), groupCount);
        }

        /// <summary>Records a dispatch with an explicit typed root struct for <paramref name="shader"/>.</summary>
        public unsafe void Dispatch<TRoot>(Shader shader, in TRoot root, Extent3D groupCount) where TRoot : unmanaged
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert((uint)sizeof(TRoot) >= shader.Root.SizeBytes);
            _context.BindCompute(shader, null);
            _context.RecordingCommands.Dispatch(_context.AllocateRawRoot(in root), groupCount);
        }

        /// <summary>Records a dispatch with a prepacked root <see cref="BufferSlice"/> for <paramref name="material"/>.</summary>
        public void Dispatch(Material material, BufferSlice root, Extent3D groupCount)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            GPURange rootRange = _context.ValidateRootSlice(root, material.Root.SizeBytes);
            _context.BindCompute(material.Shader, material);
            _context.RecordingCommands.Dispatch(rootRange, groupCount);
        }

        /// <summary>Records a dispatch with a prepacked root <see cref="BufferSlice"/> for <paramref name="shader"/>.</summary>
        public void Dispatch(Shader shader, BufferSlice root, Extent3D groupCount)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            GPURange rootRange = _context.ValidateRootSlice(root, shader.Root.SizeBytes);
            _context.BindCompute(shader, null);
            _context.RecordingCommands.Dispatch(rootRange, groupCount);
        }

        internal void DispatchLowLevel(Material material, GPURange root, Extent3D groupCount)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert(material != null && root.Buffer != null && root.Offset % BumpAllocator.Alignment == 0 && root.Size >= material.Root.SizeBytes);
            _context.BindCompute(material.Shader, material);
            _context.RecordingCommands.Dispatch(root, groupCount);
        }

        internal void DispatchLowLevel(Shader shader, GPURange root, Extent3D groupCount)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert(root.Buffer != null && root.Offset % BumpAllocator.Alignment == 0 && root.Size >= shader.Root.SizeBytes);
            _context.BindCompute(shader, null);
            _context.RecordingCommands.Dispatch(root, groupCount);
        }

        /// <summary>Records an indirect dispatch with a fresh root snapshot.</summary>
        public void DispatchIndirect(Material material, Buffer arguments, ulong byteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert(!arguments.IsDisposed && arguments.System == _context._system && byteOffset < arguments.SizeBytes);
            GPURange root = _context.PrepareDispatch(material);
            _context.RecordingCommands.DispatchIndirect(root, arguments.LowLevelRange.Slice(byteOffset));
        }

        /// <summary>Records an indirect dispatch with a fresh root snapshot and argument slice.</summary>
        public void DispatchIndirect(Material material, BufferSlice arguments)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            GPURange root = _context.PrepareDispatch(material);
            _context.RecordingCommands.DispatchIndirect(root, argRange);
        }

        /// <summary>Records an indirect dispatch with an explicit typed root struct for <paramref name="material"/>.</summary>
        public unsafe void DispatchIndirect<TRoot>(Material material, in TRoot root, BufferSlice arguments) where TRoot : unmanaged
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert((uint)sizeof(TRoot) >= material.Root.SizeBytes);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            _context.BindCompute(material.Shader, material);
            _context.RecordingCommands.DispatchIndirect(_context.AllocateRawRoot(in root), argRange);
        }

        /// <summary>Records an indirect dispatch with an explicit typed root struct for <paramref name="shader"/>.</summary>
        public unsafe void DispatchIndirect<TRoot>(Shader shader, in TRoot root, BufferSlice arguments) where TRoot : unmanaged
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert((uint)sizeof(TRoot) >= shader.Root.SizeBytes);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            _context.BindCompute(shader, null);
            _context.RecordingCommands.DispatchIndirect(_context.AllocateRawRoot(in root), argRange);
        }

        /// <summary>Records an indirect dispatch with a prepacked root <see cref="BufferSlice"/> for <paramref name="material"/>.</summary>
        public void DispatchIndirect(Material material, BufferSlice root, BufferSlice arguments)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            GPURange rootRange = _context.ValidateRootSlice(root, material.Root.SizeBytes);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            _context.BindCompute(material.Shader, material);
            _context.RecordingCommands.DispatchIndirect(rootRange, argRange);
        }

        /// <summary>Records an indirect dispatch with a prepacked root <see cref="BufferSlice"/> for <paramref name="shader"/>.</summary>
        public void DispatchIndirect(Shader shader, BufferSlice root, BufferSlice arguments)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            GPURange rootRange = _context.ValidateRootSlice(root, shader.Root.SizeBytes);
            GPURange argRange = _context.ValidateBufferSlice(arguments);
            _context.BindCompute(shader, null);
            _context.RecordingCommands.DispatchIndirect(rootRange, argRange);
        }

        internal void DispatchIndirectLowLevel(Material material, GPURange root, Buffer arguments, ulong byteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert(material != null && root.Buffer != null && root.Offset % BumpAllocator.Alignment == 0 && root.Size >= material.Root.SizeBytes);
            Debug.Assert(!arguments.IsDisposed && arguments.System == _context._system && byteOffset < arguments.SizeBytes);
            _context.BindCompute(material.Shader, material);
            _context.RecordingCommands.DispatchIndirect(root, arguments.LowLevelRange.Slice(byteOffset));
        }

        internal void DispatchIndirectLowLevel(Shader shader, GPURange root, Buffer arguments, ulong byteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert(root.Buffer != null && root.Offset % BumpAllocator.Alignment == 0 && root.Size >= shader.Root.SizeBytes);
            Debug.Assert(!arguments.IsDisposed && arguments.System == _context._system && byteOffset < arguments.SizeBytes);
            _context.BindCompute(shader, null);
            _context.RecordingCommands.DispatchIndirect(root, arguments.LowLevelRange.Slice(byteOffset));
        }

        /// <inheritdoc cref="GPUCommandBuffer.Barrier"/>
        public void Barrier(Stage before, Access beforeAccess, Stage after, Access afterAccess)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            _context.RecordingCommands.Barrier(before, beforeAccess, after, afterAccess);
        }

        /// <summary>Closes compute recording without submitting commands or inserting a barrier.</summary>
        public void End()
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            _context.ResetScope();
        }

        /// <summary>Closes this scope if still active, without submitting commands or inserting a barrier.</summary>
        public void Dispose()
        {
            if (_context != null && _context.IsScopeActive(ScopeKind.Compute, _id))
                _context.ResetScope();
        }
    }
}
