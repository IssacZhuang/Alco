// Example-level frame recording and submission with render and compute scopes and per-frame storage.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Owns two queue-zero recording slots; each concurrent recording task needs its own context.</summary>
/// <remarks>Serialize submissions with all other queue-zero operations that submit work.</remarks>
public sealed class RenderContext : RenderingResource
{
    private sealed class FrameSlot
    {
        internal readonly GPUCommandPool Pool;
        internal readonly BumpAllocator RootData;
        internal GPUHeap UploadHeap;
        internal BumpAllocator? UploadData;
        internal TimelinePoint Completion;
        internal ulong SubmissionValue;
        internal ulong FrameId;

        internal FrameSlot(GPUDevice device, GPUAllocation<byte> storage)
        {
            RootData = new BumpAllocator(storage);
            GPUCommandPool pool = device.CreateCommandPool(0);
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
    private readonly ulong _uploadByteCount;
    private int _frameIndex;
    private ulong _nextFrameId;
    private ulong _activeFrameId;
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
    private readonly List<RenderingResource> _resources = new(32);
    private Material? _retainedResourceOwner;
    private uint _retainedResourceVersion;
    private Buffer? _retainedIndices;

    /// <summary>Creates and prewarms two command pools with independent root storage; failed creation releases partial resources.</summary>
    /// <param name="system">The rendering system supplying the device and descriptor heaps.</param>
    /// <param name="frameDataByteCount">The root storage capacity per slot, a positive multiple of 16 bytes.</param>
    /// <param name="uploadByteCount">The lazily allocated upload capacity per slot, a positive multiple of 16 bytes.</param>
    /// <exception cref="GPUException">Command or root storage could not be created.</exception>
    public RenderContext(RenderingSystem system, ulong frameDataByteCount = 64 * 1024, ulong uploadByteCount = 4 * 1024 * 1024) : base(system)
    {
        Debug.Assert(frameDataByteCount != 0 && frameDataByteCount % BumpAllocator.Alignment == 0);
        Debug.Assert(uploadByteCount != 0 && uploadByteCount % BumpAllocator.Alignment == 0);
        _uploadByteCount = uploadByteCount;
        GPUHeap rootHeap = system.Device.CreateGPUHeap(frameDataByteCount * 2);
        FrameSlot[] frames = new FrameSlot[2];
        BumpAllocator storage = new(rootHeap.Range);
        int frameCount = 0;
        try
        {
            for (; frameCount < frames.Length; ++frameCount)
                frames[frameCount] = new FrameSlot(system.Device, storage.Allocate(frameDataByteCount));
        }
        catch (GPUException)
        {
            for (int index = frameCount - 1; index >= 0; --index)
                frames[index].Pool.Destroy();
            rootHeap.Destroy();
            throw;
        }
        _rootHeap = rootHeap;
        _frames = frames;
    }

    private GPUCommandBuffer RecordingCommands
    {
        get
        {
            Debug.Assert(!IsDestroyed && _recording && _commands != null);
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

    /// <summary>Monotonic identifier of the active recording frame, or zero when no frame is recording.</summary>
    public ulong CurrentFrameId => _activeFrameId;

    /// <summary>Number of timestamps recorded in the active frame.</summary>
    public int TimestampCount => _recordedTimestampCount;

    /// <summary>Reports whether <paramref name="frameId"/> is still recording or awaiting slot reclamation and timestamp collection.</summary>
    /// <param name="frameId">A frame identifier previously read from <see cref="CurrentFrameId"/>.</param>
    /// <returns><see langword="true"/> while the frame's slot and recorded timestamp destinations remain in use.</returns>
    public bool IsFramePending(ulong frameId)
    {
        if (frameId == 0 || IsDestroyed)
            return false;
        for (int index = 0; index < _frames.Length; ++index)
        {
            if (_frames[index].FrameId == frameId)
                return true;
        }
        return false;
    }

    private bool HasRenderPass => _scope is ScopeKind.Render or ScopeKind.PreparedRender;

    /// <summary>The current frame allocator; do not reset it or use its allocations after this slot is recycled.</summary>
    public BumpAllocator FrameData
    {
        get
        {
            Debug.Assert(!IsDestroyed && _recording);
            return _frames[_frameIndex].RootData;
        }
    }

    /// <summary>Waits for the next slot, collects its timestamps, and begins recording; resolve the previous frame before calling.</summary>
    /// <exception cref="GPUException">Command recording could not begin.</exception>
    public void BeginFrame()
    {
        Debug.Assert(!IsDestroyed && _commands == null);
        _system.Tick();
        FrameSlot frame = _frames[_frameIndex];
        Reclaim(frame);
        frame.Pool.Reset();
        frame.RootData.Reset();
        frame.UploadData?.Reset();
        _commands = frame.Pool.BeginCommands();
        _recording = true;
        frame.FrameId = ++_nextFrameId;
        _activeFrameId = frame.FrameId;
        _recordedTimestampCount = 0;
    }

    /// <summary>Ends and submits the active frame without presenting or waiting for GPU completion. Close all scopes first.</summary>
    /// <param name="waits">The GPU dependencies for this submission.</param>
    /// <returns>The frame's completion point, borrowing the system's timeline. Do not signal or destroy it.</returns>
    /// <exception cref="GPUException">Initialization command recording could not begin.</exception>
    public TimelinePoint Submit(scoped ReadOnlySpan<TimelinePoint> waits = default)
    {
        return _system.Submit([this], waits);
    }

    /// <summary>Ends and submits the active frame and presents the specified image. Close all scopes first.</summary>
    /// <param name="swapchain">The swapchain whose acquired image to present.</param>
    /// <param name="waits">The GPU dependencies for this submission.</param>
    /// <returns>The frame's GPU completion point, borrowing the system's timeline. Do not signal or destroy it.</returns>
    /// <exception cref="GPUException">Initialization command recording could not begin.</exception>
    public TimelinePoint SubmitAndPresent(GPUSwapchain swapchain, scoped ReadOnlySpan<TimelinePoint> waits = default)
    {
        Debug.Assert(swapchain != null);
        return _system.SubmitPrepared([this], swapchain, waits, _system.PrepareSubmission());
    }

    /// <summary>Ends recording after all scopes close and flushes pending texture initialization for a caller-managed submission.</summary>
    /// <remarks>Submit this frame once and call <see cref="MarkSubmitted"/>, or discard it.</remarks>
    /// <returns>The borrowed, ended command buffer for submission only.</returns>
    /// <exception cref="GPUException">Initialization command recording could not begin.</exception>
    public GPUCommandBuffer EndFrame()
    {
        GPUCommandBuffer commands = EndRecording();
        _system.FlushInitializations();
        return commands;
    }

    /// <summary>Registers the completion covering all uses of a caller-submitted frame and advances to the other slot.</summary>
    /// <param name="completion">A completion ordered after all frame uses; retain its semaphore until this slot is reclaimed or waited.</param>
    /// <remarks>Material draws and dispatches retain their bound resources through this call. Prepared passes and copies borrow their operands.</remarks>
    public void MarkSubmitted(TimelinePoint completion)
    {
        Debug.Assert(!IsDestroyed && !_recording && _commands != null && completion.Semaphore != null);
        RegisterSubmission(completion, _system.OnSubmitted(completion));
    }

    internal GPUCommandBuffer EndRecording()
    {
        GPUCommandBuffer commands = CommandsBetweenScopes;
        commands.EndCommands();
        _recording = false;
        return commands;
    }

    internal void RegisterSubmission(TimelinePoint completion, ulong submissionValue)
    {
        FrameSlot frame = _frames[_frameIndex];
        frame.Completion = completion;
        frame.SubmissionValue = submissionValue;
        ClearResources();
        _commands = null;
        _activeFrameId = 0;
        _recordedTimestampCount = 0;
        _frameIndex = (_frameIndex + 1) % _frames.Length;
    }

    /// <summary>Discards an unsubmitted frame and invalidates its scopes, commands, and data; swapchain cancellation remains caller-owned.</summary>
    public void DiscardFrame()
    {
        Debug.Assert(!IsDestroyed);
        if (_commands == null)
            return;
        if (HasRenderPass)
            EndRenderPass();
        if (_recording)
            _commands.EndCommands();
        FrameSlot frame = _frames[_frameIndex];
        frame.Pool.Reset();
        frame.RootData.Reset();
        frame.UploadData?.Reset();
        frame.FrameId = 0;
        ClearResources();
        _commands = null;
        _recording = false;
        _activeFrameId = 0;
        _recordedTimestampCount = 0;
        ResetScope();
    }

    /// <summary>Waits for submitted slots and collects timestamps without submitting the current frame.</summary>
    public void Wait()
    {
        Debug.Assert(!IsDestroyed);
        foreach (FrameSlot frame in _frames)
            Reclaim(frame);
    }

    /// <summary>Destroys the command pools and root storage after waiting idle; safe at any point once no frame is being recorded.</summary>
    protected override void Release()
    {
        // A partially constructed context (constructor failure) owns nothing: its cleanup already rolled back.
        if (_frames == null)
            return;
        // A reclaimed context may have unknown in-flight state; waiting idle makes its completions observable before teardown.
        _system.Device.WaitIdle();
        ClearResources();
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
        }
        _rootHeap.Destroy();
    }

    /// <summary>Acquires an image and records its initial transition between scopes.</summary>
    /// <param name="swapchain">The swapchain to acquire.</param>
    /// <returns>A frame borrowed until presentation or reconfiguration, or an empty frame while unavailable.</returns>
    public SwapchainFrame Acquire(GPUSwapchain swapchain)
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

    /// <summary>Records a buffer copy between scopes without inserting a barrier.</summary>
    /// <param name="source">The source range, kept alive until GPU completion.</param>
    /// <param name="destination">The destination range, kept alive until GPU completion.</param>
    public void CopyMemory(GPURange source, GPURange destination)
    {
        CommandsBetweenScopes.CopyMemory(source, destination);
    }

    /// <summary>Records a texture-to-buffer copy between scopes without inserting a barrier.</summary>
    /// <param name="source">The single-sample source texture, kept alive until GPU completion.</param>
    /// <param name="destination">The destination range, kept alive until GPU completion.</param>
    /// <param name="copy">The texture region and destination pitches.</param>
    public void CopyTextureToMemory(GPUTexture source, GPURange destination, TextureCopyDesc copy = default)
    {
        CommandsBetweenScopes.CopyTextureToMemory(source, destination, copy);
    }

    /// <summary>Records a non-overlapping texture copy between scopes without inserting a barrier.</summary>
    /// <param name="source">The single-sample source texture, kept alive until GPU completion.</param>
    /// <param name="destination">The single-sample destination texture, kept alive until GPU completion.</param>
    /// <param name="copy">The source and destination regions.</param>
    public void CopyTexture(GPUTexture source, GPUTexture destination, TextureRegionCopyDesc copy = default)
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
    /// <param name="texture">The destination from this context's system, retained through submission registration or discard.</param>
    /// <param name="source">The region data including pitch padding, copied before this method returns.</param>
    /// <param name="copy">The destination mip, slices, region, and memory pitches; the default writes mip zero.</param>
    /// <exception cref="InvalidOperationException">The recording frame's remaining upload capacity is insufficient.</exception>
    /// <exception cref="GPUException">Frame upload storage could not be created.</exception>
    public unsafe void SetTextureData(Texture texture, ReadOnlySpan<byte> source, in TextureCopyDesc copy = default)
    {
        GPUCommandBuffer commands = CommandsBetweenScopes;
        Debug.Assert(texture != null && !texture.IsDestroyed && texture.System == _system && !source.IsEmpty);
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
        TextureUpload.UploadTexture(commands, staging, texture.NativeTexture, texture.Desc, source, copy);
        commands.Barrier(Stage.Transfer, Access.TransferWrite, Stage.AllCommands, TextureAccess);
        RetainResource(texture);
    }

    /// <summary>Begins a render pass within the active frame, deriving attachment formats and sample count from <paramref name="desc"/>.</summary>
    /// <param name="desc">The attachments and their operations.</param>
    /// <returns>The borrowed scope, valid until the pass ends or the frame is discarded.</returns>
    public RenderScope BeginRenderPass(scoped RenderingDesc desc)
    {
        return BeginRenderPass(desc, RenderPassSignature.From(in desc));
    }

    /// <summary>Begins a render pass within the active frame using an explicit pass signature.</summary>
    /// <param name="desc">The attachments and their operations.</param>
    /// <param name="signature">The pass attachment formats and sample count.</param>
    /// <returns>The borrowed scope, valid until the pass ends or the frame is discarded.</returns>
    public RenderScope BeginRenderPass(scoped RenderingDesc desc, RenderPassSignature signature)
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

    /// <summary>Begins a render pass within the active frame. No other scope may be open.</summary>
    /// <param name="desc">The attachments and their operations.</param>
    /// <param name="colorFormats">The formats matching the color attachments in order.</param>
    /// <param name="depthFormat">The depth attachment format, or Undefined when absent.</param>
    /// <param name="stencilFormat">The stencil attachment format, or Undefined when absent.</param>
    /// <returns>The borrowed scope, valid until the pass ends or the frame is discarded.</returns>
    public RenderScope BeginRenderPass(scoped RenderingDesc desc, scoped ReadOnlySpan<Format> colorFormats,
        Format depthFormat = Format.Undefined, Format stencilFormat = Format.Undefined)
    {
        Debug.Assert(colorFormats.Length == desc.Colors.Length);
        uint sampleCount = DeriveSampleCount(in desc);
        return BeginRenderPass(desc, new RenderPassSignature(colorFormats, depthFormat, stencilFormat, sampleCount));
    }

    private static uint DeriveSampleCount(scoped in RenderingDesc desc)
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

    /// <summary>Begins a pass for prepacked roots and caller-owned bindings; do not record material draws in it.</summary>
    /// <param name="desc">The attachments and their operations, compatible with the pipeline.</param>
    /// <param name="pipeline">The graphics pipeline, kept alive until GPU completion.</param>
    /// <param name="textures">The texture descriptor heap, kept alive until GPU completion.</param>
    /// <param name="samplers">The sampler descriptor heap, kept alive until GPU completion.</param>
    /// <returns>The borrowed scope for prepared draws, with depth and stencil tests disabled.</returns>
    public RenderScope BeginPreparedRenderPass(scoped RenderingDesc desc, GPUPipeline pipeline,
        GPUTextureDescriptorHeap textures, GPUSamplerDescriptorHeap samplers)
    {
        GPUCommandBuffer commands = CommandsBetweenScopes;
        commands.SetTextureDescriptorHeap(textures);
        commands.SetSamplerDescriptorHeap(samplers);
        RenderScope render = BeginPreparedRenderPass(desc);
        commands.BindPipeline(pipeline);
        commands.SetDepthStencil(default);
        return render;
    }

    internal RenderScope BeginPreparedRenderPass(scoped RenderingDesc desc)
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
        if (!HasDepthStencil || !DepthStencilMatches(LastDepthStencil, depthStencil))
        {
            _commands.SetDepthStencil(depthStencil);
            LastDepthStencil = depthStencil;
            HasDepthStencil = true;
        }
    }

    internal static bool DepthStencilMatches(DepthStencilState left, DepthStencilState right)
    {
        return left.DepthTest == right.DepthTest && left.DepthWrite == right.DepthWrite && left.DepthCompare == right.DepthCompare &&
            left.StencilTest == right.StencilTest && left.StencilReadMask == right.StencilReadMask && left.StencilWriteMask == right.StencilWriteMask &&
            left.Front.Compare == right.Front.Compare && left.Front.Fail == right.Front.Fail && left.Front.Pass == right.Front.Pass &&
            left.Front.DepthFail == right.Front.DepthFail && left.Front.Reference == right.Front.Reference &&
            left.Back.Compare == right.Back.Compare && left.Back.Fail == right.Back.Fail && left.Back.Pass == right.Back.Pass &&
            left.Back.DepthFail == right.Back.DepthFail && left.Back.Reference == right.Back.Reference;
    }

    internal void RetainResource(RenderingResource resource)
    {
        Debug.Assert(!resource.IsDestroyed);
        _resources.Add(resource);
    }

    private void RetainIndexBuffer(Buffer indices)
    {
        if (_retainedIndices != indices)
        {
            RetainResource(indices);
            _retainedIndices = indices;
        }
    }

    private void ClearResources()
    {
        _resources.Clear();
        _retainedResourceOwner = null;
        _retainedIndices = null;
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
        Debug.Assert(!IsDestroyed && _recording && IsScopeActive(kind, id));
    }

    private GPURange PrepareDraw(Material material, ShaderKind kind = ShaderKind.Graphics)
    {
        BindMaterial(material, material.DepthStencilState, kind);
        return material.AllocateRoot(_frames[_frameIndex].RootData).GPURange;
    }

    private void BindMaterial(Material material, DepthStencilState depthStencil, ShaderKind kind = ShaderKind.Graphics)
    {
        Debug.Assert(_recording && _scope == ScopeKind.Render && _commands != null && material.Shader.System == _system);
        Shader shader = material.Shader;
        Debug.Assert(!shader.IsDestroyed && shader.Kind == kind);
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
        RetainMaterial(material);
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
        return material.AllocateRoot(_frames[_frameIndex].RootData).GPURange;
    }

    private void BindCompute(Shader shader, Material? material)
    {
        Debug.Assert(_recording && _scope == ScopeKind.Compute && _commands != null && !shader.IsDestroyed && shader.System == _system);
        ShaderVariant variant = material != null ? material.Variant : shader.DefaultVariant;
        if (_lastVariant != variant)
        {
            _commands.BindPipeline(variant.GetComputePipeline());
            _lastVariant = variant;
            if (material == null)
                RetainResource(shader);
        }
        if (material != null)
            RetainMaterial(material);
    }

    private void RetainMaterial(Material material)
    {
        Material resourceOwner = material.ResourceOwner;
        uint resourceVersion = resourceOwner.ResourceVersion;
        if (_retainedResourceOwner != resourceOwner || _retainedResourceVersion != resourceVersion)
        {
            resourceOwner.RetainResources(_resources);
            _retainedResourceOwner = resourceOwner;
            _retainedResourceVersion = resourceVersion;
        }
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

        /// <summary>Records a draw with the pipeline supplied when the prepared pass began.</summary>
        /// <param name="root">The aligned root range and its referenced resources, kept alive until GPU completion.</param>
        /// <param name="vertexCount">The number of vertices.</param>
        public void Draw(GPURange root, uint vertexCount)
        {
            PreparedCommands.Draw(root, vertexCount);
        }

        /// <summary>Records a draw with a fresh root snapshot.</summary>
        /// <param name="material">The material; its parameters and state must remain unchanged during parallel recording.</param>
        /// <param name="vertexCount">The number of vertices per instance.</param>
        /// <param name="instanceCount">The number of instances.</param>
        /// <param name="firstVertex">The index of the first vertex.</param>
        /// <param name="firstInstance">The index of the first instance.</param>
        /// <exception cref="InvalidOperationException"><see cref="RenderingSystem.PipelineMissPolicy"/> is <see cref="PipelineMissPolicy.Throw"/> and no matching pipeline is cached.</exception>
        public void Draw(Material material, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            GPURange root = _context.PrepareDraw(material);
            _context.RecordingCommands.Draw(root, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        /// <summary>Records a draw with prepacked root data and draw-local depth and stencil state.</summary>
        /// <param name="material">The material to bind; do not mutate it during parallel recording.</param>
        /// <param name="root">The full root block replacing material parameters. Keep it aligned to 16 bytes and its resources live until completion.</param>
        /// <param name="depthStencil">The depth and stencil state for this draw, without changing the material.</param>
        /// <param name="vertexCount">The number of vertices per instance.</param>
        /// <param name="instanceCount">The number of instances.</param>
        /// <param name="firstVertex">The index of the first vertex.</param>
        /// <param name="firstInstance">The index of the first instance.</param>
        /// <exception cref="InvalidOperationException"><see cref="RenderingSystem.PipelineMissPolicy"/> is <see cref="PipelineMissPolicy.Throw"/> and no matching pipeline is cached.</exception>
        public void Draw(Material material, GPURange root, DepthStencilState depthStencil, uint vertexCount,
            uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(root.Buffer != null && root.Offset % BumpAllocator.Alignment == 0 && root.Size >= material.Root.SizeBytes);
            _context.BindMaterial(material, depthStencil);
            _context.RecordingCommands.Draw(root, vertexCount, instanceCount, firstVertex, firstInstance);
        }

        /// <summary>Records an indexed draw with a fresh root snapshot.</summary>
        /// <param name="material">The material; its parameters and state must remain unchanged during parallel recording.</param>
        /// <param name="indices">The index buffer, retained until submission registration or discard.</param>
        /// <param name="type">The index element type.</param>
        /// <param name="indexCount">The number of indices per instance.</param>
        /// <param name="instanceCount">The number of instances.</param>
        /// <param name="firstIndex">The index of the first index-buffer element.</param>
        /// <param name="vertexOffset">The offset added to each fetched index.</param>
        /// <param name="firstInstance">The index of the first instance.</param>
        /// <param name="byteOffset">The index buffer's byte offset; firstIndex is relative to the resulting subrange.</param>
        /// <exception cref="InvalidOperationException"><see cref="RenderingSystem.PipelineMissPolicy"/> is <see cref="PipelineMissPolicy.Throw"/> and no matching pipeline is cached.</exception>
        public void DrawIndexed(Material material, Buffer indices, IndexType type, uint indexCount, uint instanceCount = 1,
            uint firstIndex = 0, int vertexOffset = 0, uint firstInstance = 0, ulong byteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(indices != null && !indices.IsDestroyed && indices.System == _context._system && byteOffset < indices.SizeBytes);
            GPURange root = _context.PrepareDraw(material);
            _context.RetainIndexBuffer(indices);
            _context.RecordingCommands.DrawIndexed(root, indices.Range.Slice(byteOffset), type, indexCount, instanceCount, firstIndex, vertexOffset,
                firstInstance);
        }

        /// <summary>Records indexed indirect draws with a fresh root snapshot.</summary>
        /// <param name="material">The material; its parameters and state must remain unchanged during parallel recording.</param>
        /// <param name="indices">The index buffer, retained through submission registration or discard.</param>
        /// <param name="type">The index element type.</param>
        /// <param name="arguments">The GPU-written or uploaded argument buffer, retained through submission registration or discard.</param>
        /// <param name="drawCount">The number of draw records.</param>
        /// <param name="stride">The record stride in bytes; zero selects tightly packed 20-byte records.</param>
        /// <param name="argumentByteOffset">The byte offset of the first argument record.</param>
        /// <param name="indexByteOffset">The index buffer's byte offset.</param>
        /// <exception cref="InvalidOperationException"><see cref="RenderingSystem.PipelineMissPolicy"/> is <see cref="PipelineMissPolicy.Throw"/> and no matching pipeline is cached.</exception>
        public void DrawIndexedIndirect(Material material, Buffer indices, IndexType type, Buffer arguments,
            uint drawCount = 1, uint stride = 0, ulong argumentByteOffset = 0, ulong indexByteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(indices != null && !indices.IsDestroyed && indices.System == _context._system && indexByteOffset < indices.SizeBytes);
            Debug.Assert(arguments != null && !arguments.IsDestroyed && arguments.System == _context._system && argumentByteOffset < arguments.SizeBytes);
            GPURange root = _context.PrepareDraw(material);
            _context.RetainIndexBuffer(indices);
            _context.RetainResource(arguments);
            _context.RecordingCommands.DrawIndexedIndirect(root, indices.Range.Slice(indexByteOffset), type,
                arguments.Range.Slice(argumentByteOffset), drawCount, stride);
        }

        /// <summary>Records a mesh draw with a fresh root snapshot.</summary>
        /// <param name="material">The mesh material, unchanged during parallel recording.</param>
        /// <param name="groupCount">The task or mesh workgroup counts.</param>
        public void DrawMeshlets(Material material, Extent3D groupCount)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            _context.RecordingCommands.DrawMeshlets(_context.PrepareDraw(material, ShaderKind.Mesh), groupCount);
        }

        /// <summary>Records indirect mesh draws with a fresh root snapshot.</summary>
        /// <param name="material">The mesh material, unchanged during parallel recording.</param>
        /// <param name="arguments">The argument buffer, retained through submission registration.</param>
        /// <param name="drawCount">The number of argument records.</param>
        /// <param name="stride">The byte stride, or zero for tightly packed records.</param>
        /// <param name="byteOffset">The byte offset of the first record in the buffer.</param>
        public void DrawMeshletsIndirect(Material material, Buffer arguments, uint drawCount = 1, uint stride = 0, ulong byteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(!arguments.IsDestroyed && arguments.System == _context._system && byteOffset < arguments.SizeBytes);
            GPURange root = _context.PrepareDraw(material, ShaderKind.Mesh);
            _context.RetainResource(arguments);
            _context.RecordingCommands.DrawMeshletsIndirect(root, arguments.Range.Slice(byteOffset), drawCount, stride);
        }

        /// <summary>Replays sealed draws in the current compatible pass, without allocating frame roots.</summary>
        /// <param name="bundle">The bundle recorded for this system and pass formats, retained through submission registration.</param>
        public void ExecuteBundle(RenderBundle bundle)
        {
            _context.AssertScope(ScopeKind.Render, _id);
            Debug.Assert(bundle.IsCompatible(_context._system, _context._passSignature));
            _context.RetainResource(bundle);
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
        /// <param name="material">The compute material, unchanged during parallel recording.</param>
        /// <param name="groupCount">The number of workgroups along each axis.</param>
        public void Dispatch(Material material, Extent3D groupCount)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            _context.RecordingCommands.Dispatch(_context.PrepareDispatch(material), groupCount);
        }

        /// <summary>Records a dispatch with prepacked data matching the compute shader's root layout.</summary>
        /// <param name="shader">The compute shader, retained through submission registration.</param>
        /// <param name="root">The aligned root block and its referenced resources; the caller retains them through GPU completion.</param>
        /// <param name="groupCount">The number of workgroups along each axis.</param>
        public void Dispatch(Shader shader, GPURange root, Extent3D groupCount)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert(root.Buffer != null && root.Offset % BumpAllocator.Alignment == 0 && root.Size >= shader.Root.SizeBytes);
            _context.BindCompute(shader, null);
            _context.RecordingCommands.Dispatch(root, groupCount);
        }

        /// <summary>Records an indirect dispatch with a fresh root snapshot.</summary>
        /// <param name="material">The compute material, unchanged during parallel recording.</param>
        /// <param name="arguments">The argument buffer, retained through submission registration.</param>
        /// <param name="byteOffset">The byte offset of the dispatch record.</param>
        public void DispatchIndirect(Material material, Buffer arguments, ulong byteOffset = 0)
        {
            _context.AssertScope(ScopeKind.Compute, _id);
            Debug.Assert(!arguments.IsDestroyed && arguments.System == _context._system && byteOffset < arguments.SizeBytes);
            GPURange root = _context.PrepareDispatch(material);
            _context.RetainResource(arguments);
            _context.RecordingCommands.DispatchIndirect(root, arguments.Range.Slice(byteOffset));
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
