// Explicit-synchronization render graph with dead-pass culling, lifetime-aliased resource pooling, and barrier planning.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>
/// Compiles and executes an ordered list of <see cref="IRenderGraphNode"/> passes with dead-pass culling,
/// transient texture and buffer pooling with lifetime aliasing, explicit barrier planning, and optional GPU profiling.
/// Owns only its pooled resources and profiling buffers; callers retain ownership of nodes and imported resources.
/// </summary>
public sealed unsafe class RenderGraph : IDisposable
{
    private const int TimestampSlotCount = 4;
    private const int DefaultTimestampQueryBudget = 256;

    private enum ResourceKind : byte
    {
        None,
        TransientTexture,
        TransientBuffer,
        ImportedTexture,
        ImportedBuffer,
        SwapchainOutput,
    }

    private struct ResourceSlot
    {
        internal bool Active;
        internal bool IsRetained;
        internal bool IsTexture;
        internal ResourceKind Kind;
        internal ushort Generation;
        internal string Name;
        internal RGTextureDesc TextureDesc;
        internal ulong BufferSizeBytes;
        internal Texture? ExternalTexture;
        internal Buffer? ExternalBuffer;
        internal object? ExternalKey;
        internal RGUsage FinalUsage;
        internal RGUsage FrameFinalUsage;
        internal int StickyPoolIndex;

        // Per-frame compiled state.
        internal Extent3D ResolvedExtent;
        internal Format Format;
        internal TextureUsage ExplicitUsage;
        internal TextureUsage InferredUsage;
        internal bool HasWriter;
        internal bool Needed;
        internal bool TouchedByAlivePass;
        internal int FirstPass;
        internal int LastPass;
        internal PhysicalRef Physical;
    }

    private struct PassAccess
    {
        internal int ResourceIndex;
        internal RGUsage Usage;
        internal bool IsLogicalRead;
        internal bool IsLogicalWrite;
        internal int NextAccess;
    }

    private struct PassRecord
    {
        internal string Name;
        internal IRenderGraphNode Node;
        internal int NodeIndex;
        internal int PassId;
        internal int FirstAccess;
        internal int LastAccess;
        internal bool ProducesOutput;
        internal bool Alive;
        internal RGBarrier Barrier;
        internal int ReadCount;
        internal int WriteCount;
        internal double CpuMilliseconds;
        internal double GpuMilliseconds;
    }

    private struct TimestampSlot
    {
        internal ulong* Values;
        internal int Capacity;
        internal int Count;
        internal int[]? NodeIndices;
        internal int[]? PassIds;
        internal string[]? PassNames;
        internal RenderContext? Context;
        internal ulong ContextFrameId;
        internal bool Pending;
    }

    private struct CachedPassTiming
    {
        internal int NodeIndex;
        internal int PassId;
        internal string Name;
        internal double GpuMilliseconds;
    }

    private readonly RenderingSystem? _system;
    private readonly RenderGraphPools _pools;
    private readonly RenderGraphBuilder _builder = new();
    private readonly List<IRenderGraphNode> _nodes = [];

    // Slot 0 is reserved for the frame output target; slots 1.._retainedHighWater-1 are retained resources;
    // slots _frameResourceBase.._resourceCount-1 are frame-scoped resources created during Setup.
    private ResourceSlot[] _resources = new ResourceSlot[32];
    private int _retainedHighWater = 1;
    private int _frameResourceBase = 1;
    private int _resourceCount = 1;
    private ushort _outputGeneration;
    private ushort _frameGeneration;

    private PassRecord[] _passes = new PassRecord[16];
    private int _passCount;
    private PassAccess[] _accesses = new PassAccess[64];
    private int _accessCount;
    private int[] _allocOrder = new int[32];
    private int _allocCount;
    private int[] _activeTransients = new int[32];
    private int _activeTransientCount;

    private RenderGraphPassReport[] _passReports = new RenderGraphPassReport[16];
    private int _passReportCount;
    private RGBarrier _exitBarrier;
    private RenderGraphStatistics _statistics;

    private TimestampSlot[]? _timestampSlots;
    private int _timestampNextSlot;
    private CachedPassTiming[] _cachedTimings = new CachedPassTiming[16];
    private int _cachedTimingCount;

    private Extent2D _viewport;
    private RenderGraphTarget _currentOutput;
    private RenderContext? _lastContext;
    private ulong _lastContextDiscardVersion;
    private ulong _frameNumber;
    private bool _inSetup;
    private bool _inExecute;
    private bool _destroyed;

    /// <summary>Creates a render graph backed by the given <see cref="RenderingSystem"/> and initial viewport extent.</summary>
    /// <param name="system">The rendering system supplying pooled textures, buffers, and device capabilities.</param>
    /// <param name="viewport">The initial logical viewport extent in pixels; both dimensions must be positive.</param>
    /// <param name="name">Optional diagnostic name for error messages and reports.</param>
    public RenderGraph(RenderingSystem system, Extent2D viewport, string name = "RenderGraph")
    {
        ArgumentNullException.ThrowIfNull(system);
        ArgumentNullException.ThrowIfNull(name);
        if (viewport.X == 0 || viewport.Y == 0)
            throw new ArgumentOutOfRangeException(nameof(viewport), "RenderGraph viewport dimensions must be positive.");
        _system = system;
        _pools = new RenderGraphPools(system);
        _viewport = viewport;
        Name = name;
    }

    // Dry-run constructor for CPU-only compilation, aliasing, culling, and barrier unit tests.
    internal RenderGraph(Extent2D viewport, string name = "DryRunGraph")
    {
        ArgumentNullException.ThrowIfNull(name);
        if (viewport.X == 0 || viewport.Y == 0)
            throw new ArgumentOutOfRangeException(nameof(viewport), "RenderGraph viewport dimensions must be positive.");
        _system = null;
        _pools = new RenderGraphPools(null);
        _viewport = viewport;
        Name = name;
    }

    /// <summary>Diagnostic name of this graph.</summary>
    public string Name { get; }

    /// <summary>The current logical viewport extent in pixels.</summary>
    public Extent2D Viewport => _viewport;

    /// <summary>The owning rendering system.</summary>
    public RenderingSystem System => _system ?? throw new InvalidOperationException("Dry-run RenderGraph has no RenderingSystem.");

    /// <summary>Whether <see cref="Dispose"/> has been called on this graph.</summary>
    public bool IsDisposed => _destroyed;

    /// <summary>Legacy alias for <see cref="IsDisposed"/>.</summary>
    [Obsolete("Use IsDisposed instead.")]
    public bool IsDestroyed => _destroyed;

    /// <summary>Whether the most recent or active frame had a valid output target.</summary>
    public bool HasOutputThisFrame { get; private set; }

    /// <summary>Enables per-pass CPU stopwatch and GPU timestamp profiling.</summary>
    public bool ProfilingEnabled { get; set; }

    /// <summary>Maximum number of alive passes timestamped per frame when <see cref="ProfilingEnabled"/> is true.</summary>
    public int MaxTimestampPasses { get; set; } = 64;

    /// <summary>Number of consecutive unused frames after which an idle pooled resource is retired; zero disables eviction.</summary>
    public int IdleFrameRetireThreshold { get; set; } = 60;

    /// <summary>Registered nodes in execution order.</summary>
    public IReadOnlyList<IRenderGraphNode> Nodes => _nodes;

    /// <summary>Per-pass compilation, barrier, and timing reports from the most recent frame.</summary>
    public ReadOnlySpan<RenderGraphPassReport> PassReports => _passReports.AsSpan(0, _passReportCount);

    /// <summary>The trailing barrier emitted at the end of the most recent frame for external handoff usages.</summary>
    public RGBarrier ExitBarrier => _exitBarrier;

    /// <summary>Summary statistics from the most recent frame.</summary>
    public RenderGraphStatistics Statistics => _statistics;

    internal RGTexture CurrentOutputHandle => HasOutputThisFrame ? new RGTexture(0, _outputGeneration) : default;

    /// <summary>Appends a node to the end of the graph.</summary>
    /// <param name="node">The node to append.</param>
    /// <returns>This graph for fluent chaining.</returns>
    public RenderGraph Use(IRenderGraphNode node)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(node);
        _nodes.Add(node);
        _cachedTimingCount = 0;
        return this;
    }

    /// <summary>Inserts a node immediately before the first registered node of type <typeparamref name="TAnchor"/>.</summary>
    /// <typeparam name="TAnchor">The anchor node type.</typeparam>
    /// <param name="node">The node to insert.</param>
    /// <returns>This graph for fluent chaining.</returns>
    public RenderGraph InsertBefore<TAnchor>(IRenderGraphNode node) where TAnchor : class, IRenderGraphNode
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(node);
        int anchorIndex = FindNodeIndex<TAnchor>();
        if (anchorIndex < 0)
            throw new InvalidOperationException($"RenderGraph '{Name}': anchor node of type '{typeof(TAnchor).Name}' is not registered.");
        _nodes.Insert(anchorIndex, node);
        _cachedTimingCount = 0;
        return this;
    }

    /// <summary>Inserts a node immediately after the first registered node of type <typeparamref name="TAnchor"/>.</summary>
    /// <typeparam name="TAnchor">The anchor node type.</typeparam>
    /// <param name="node">The node to insert.</param>
    /// <returns>This graph for fluent chaining.</returns>
    public RenderGraph InsertAfter<TAnchor>(IRenderGraphNode node) where TAnchor : class, IRenderGraphNode
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(node);
        int anchorIndex = FindNodeIndex<TAnchor>();
        if (anchorIndex < 0)
            throw new InvalidOperationException($"RenderGraph '{Name}': anchor node of type '{typeof(TAnchor).Name}' is not registered.");
        _nodes.Insert(anchorIndex + 1, node);
        _cachedTimingCount = 0;
        return this;
    }

    /// <summary>Removes a registered node without destroying it.</summary>
    /// <param name="node">The node instance to remove.</param>
    /// <returns><see langword="true"/> if the node was present and removed.</returns>
    public bool Remove(IRenderGraphNode node)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(node);
        bool removed = _nodes.Remove(node);
        if (removed)
            _cachedTimingCount = 0;
        return removed;
    }

    /// <summary>Returns the first registered node of type <typeparamref name="T"/>, or null if absent.</summary>
    /// <typeparam name="T">The node type to locate.</typeparam>
    /// <returns>The matching node, or null.</returns>
    public T? Get<T>() where T : class, IRenderGraphNode
    {
        for (int index = 0; index < _nodes.Count; ++index)
        {
            if (_nodes[index] is T typed)
                return typed;
        }
        return null;
    }

    /// <summary>Registers a retained transient texture handle that remains valid across frames until released.</summary>
    /// <param name="desc">The transient texture descriptor.</param>
    /// <returns>A retained texture handle.</returns>
    public RGTexture CreateTransientTexture(in RGTextureDesc desc)
    {
        EnsureMutable();
        ValidateTransientTextureDesc(in desc);
        int index = AllocateRetainedSlot();
        ushort generation = NextGeneration(_resources[index].Generation);
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = true,
            IsTexture = true,
            Kind = ResourceKind.TransientTexture,
            Generation = generation,
            Name = string.IsNullOrEmpty(desc.Name) ? "unnamed_texture" : desc.Name,
            TextureDesc = desc,
            StickyPoolIndex = -1,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGTexture((ushort)index, generation);
    }

    /// <summary>Registers a retained transient buffer handle that remains valid across frames until released.</summary>
    /// <param name="desc">The transient buffer descriptor.</param>
    /// <returns>A retained buffer handle.</returns>
    public RGBuffer CreateTransientBuffer(in RGBufferDesc desc)
    {
        EnsureMutable();
        ValidateTransientBufferDesc(in desc);
        int index = AllocateRetainedSlot();
        ushort generation = NextGeneration(_resources[index].Generation);
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = true,
            IsTexture = false,
            Kind = ResourceKind.TransientBuffer,
            Generation = generation,
            Name = string.IsNullOrEmpty(desc.Name) ? "unnamed_buffer" : desc.Name,
            BufferSizeBytes = desc.SizeBytes,
            StickyPoolIndex = -1,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGBuffer((ushort)index, generation);
    }

    /// <summary>Registers a retained imported texture handle without taking ownership of <paramref name="texture"/>.</summary>
    /// <param name="texture">The caller-owned texture to track across frames.</param>
    /// <param name="name">Optional diagnostic name.</param>
    /// <param name="finalUsage">Optional external access performed after graph execution to emit a trailing barrier for.</param>
    /// <returns>A retained texture handle.</returns>
    public RGTexture ImportTexture(Texture texture, string name = "", RGUsage finalUsage = RGUsage.None)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(texture);
        Debug.Assert(!texture.IsDisposed);
        ValidateFinalUsage(finalUsage, isTexture: true, texture.Desc.Usage, string.IsNullOrEmpty(name) ? "imported_texture" : name);
        int index = AllocateRetainedSlot();
        ushort generation = NextGeneration(_resources[index].Generation);
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = true,
            IsTexture = true,
            Kind = ResourceKind.ImportedTexture,
            Generation = generation,
            Name = string.IsNullOrEmpty(name) ? "imported_texture" : name,
            ExternalTexture = texture,
            ExternalKey = texture,
            FinalUsage = finalUsage,
            StickyPoolIndex = -1,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGTexture((ushort)index, generation);
    }

    /// <summary>Registers a retained imported buffer handle without taking ownership of <paramref name="buffer"/>.</summary>
    /// <param name="buffer">The caller-owned buffer to track across frames.</param>
    /// <param name="name">Optional diagnostic name.</param>
    /// <param name="finalUsage">Optional external access performed after graph execution to emit a trailing barrier for.</param>
    /// <returns>A retained buffer handle.</returns>
    public RGBuffer ImportBuffer(Buffer buffer, string name = "", RGUsage finalUsage = RGUsage.None)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(buffer);
        Debug.Assert(!buffer.IsDisposed);
        ValidateFinalUsage(finalUsage, isTexture: false, TextureUsage.None, string.IsNullOrEmpty(name) ? "imported_buffer" : name);
        int index = AllocateRetainedSlot();
        ushort generation = NextGeneration(_resources[index].Generation);
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = true,
            IsTexture = false,
            Kind = ResourceKind.ImportedBuffer,
            Generation = generation,
            Name = string.IsNullOrEmpty(name) ? "imported_buffer" : name,
            BufferSizeBytes = buffer.SizeBytes,
            ExternalBuffer = buffer,
            ExternalKey = buffer,
            FinalUsage = finalUsage,
            StickyPoolIndex = -1,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGBuffer((ushort)index, generation);
    }

    /// <summary>Rebinds an existing retained imported texture handle to a new backing <see cref="Texture"/>.</summary>
    /// <param name="handle">The retained imported texture handle.</param>
    /// <param name="texture">The new live texture.</param>
    /// <param name="finalUsage">Optional external access performed after graph execution.</param>
    public void UpdateImportedTexture(RGTexture handle, Texture texture, RGUsage finalUsage = RGUsage.None)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(texture);
        Debug.Assert(!texture.IsDisposed);
        int index = ValidateTextureHandle(handle);
        ref ResourceSlot slot = ref _resources[index];
        if (!slot.IsRetained || slot.Kind != ResourceKind.ImportedTexture)
            throw new InvalidOperationException($"RenderGraph '{Name}': handle '{slot.Name}' is not a retained imported texture.");
        ValidateFinalUsage(finalUsage, isTexture: true, texture.Desc.Usage, slot.Name);
        slot.ExternalTexture = texture;
        slot.ExternalKey = texture;
        slot.FinalUsage = finalUsage;
    }

    /// <summary>Rebinds an existing retained imported buffer handle to a new backing <see cref="Buffer"/>.</summary>
    /// <param name="handle">The retained imported buffer handle.</param>
    /// <param name="buffer">The new live buffer.</param>
    /// <param name="finalUsage">Optional external access performed after graph execution.</param>
    public void UpdateImportedBuffer(RGBuffer handle, Buffer buffer, RGUsage finalUsage = RGUsage.None)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(buffer);
        Debug.Assert(!buffer.IsDisposed);
        int index = ValidateBufferHandle(handle);
        ref ResourceSlot slot = ref _resources[index];
        if (!slot.IsRetained || slot.Kind != ResourceKind.ImportedBuffer)
            throw new InvalidOperationException($"RenderGraph '{Name}': handle '{slot.Name}' is not a retained imported buffer.");
        ValidateFinalUsage(finalUsage, isTexture: false, TextureUsage.None, slot.Name);
        slot.ExternalBuffer = buffer;
        slot.ExternalKey = buffer;
        slot.BufferSizeBytes = buffer.SizeBytes;
        slot.FinalUsage = finalUsage;
    }

    /// <summary>Releases a retained texture handle so its slot can be reused and stale references are rejected.</summary>
    /// <param name="handle">The retained texture handle to release.</param>
    public void ReleaseResource(RGTexture handle)
    {
        EnsureMutable();
        int index = ValidateTextureHandle(handle);
        if (!_resources[index].IsRetained)
            throw new InvalidOperationException($"RenderGraph '{Name}': only retained handles can be released explicitly.");
        ReleaseRetainedSlot(index);
    }

    /// <summary>Releases a retained buffer handle so its slot can be reused and stale references are rejected.</summary>
    /// <param name="handle">The retained buffer handle to release.</param>
    public void ReleaseResource(RGBuffer handle)
    {
        EnsureMutable();
        int index = ValidateBufferHandle(handle);
        if (!_resources[index].IsRetained)
            throw new InvalidOperationException($"RenderGraph '{Name}': only retained handles can be released explicitly.");
        ReleaseRetainedSlot(index);
    }

    /// <summary>
    /// Compiles the graph for one frame (setup, validation, culling, lifetime aliasing, and barrier planning) and
    /// records all surviving passes and explicit barriers onto <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The recording render context; must be between render and compute scopes.</param>
    /// <param name="output">The frame's output target (swapchain or offscreen texture), or <see langword="default"/> for headless runs.</param>
    /// <param name="deltaTime">Frame delta time in seconds forwarded to <see cref="RenderGraphContext.DeltaTime"/>.</param>
    /// <param name="outputFinalUsage">
    /// Optional external access performed on <paramref name="output"/> after graph exit (for example,
    /// <see cref="RGUsage.ColorAttachment"/> before an ImGui overlay or <see cref="RGUsage.PixelRead"/> before sampling an offscreen preview).
    /// </param>
    public void Execute(RenderContext context, RenderGraphTarget output = default, float deltaTime = 0.0f, RGUsage outputFinalUsage = RGUsage.None)
    {
        EnsureNotDestroyed();
        ArgumentNullException.ThrowIfNull(context);
        if (_system == null)
            throw new InvalidOperationException("Cannot execute GPU commands on a dry-run RenderGraph.");
        Debug.Assert(!_inSetup && !_inExecute);
        Debug.Assert(context.System == _system);
        Debug.Assert(context.CurrentFrameId != 0, "RenderContext.BeginFrame must be called before RenderGraph.Execute.");

        try
        {
            if (_lastContext != null && (_lastContext.IsDisposed || _lastContext.DiscardVersion != _lastContextDiscardVersion))
                _pools.ClearSyncedReads();

            FrameUse frameUse = new(context, context.CurrentFrameId);
            CompileFrame(output, outputFinalUsage, dryRun: false, in frameUse);
            _lastContext = context;
            _lastContextDiscardVersion = context.DiscardVersion;
            ExecuteCompiledPasses(context, output, deltaTime);
        }
        finally
        {
            ClearFrameSlotReferences();
        }
    }

    /// <summary>Notifies the graph that all previously submitted GPU work has completed (for example, after <see cref="GPUDevice.WaitIdle"/>).</summary>
    public void MarkCompleted()
    {
        EnsureNotDestroyed();
        _lastContext = null;
        _lastContextDiscardVersion = 0;
        _pools.MarkCompleted();
        if (_timestampSlots != null)
        {
            for (int index = 0; index < _timestampSlots.Length; ++index)
            {
                ref TimestampSlot slot = ref _timestampSlots[index];
                if (slot.Context == null || !slot.Context.IsFramePending(slot.ContextFrameId))
                {
                    slot.Pending = false;
                    slot.Context = null;
                    slot.ContextFrameId = 0;
                }
            }
        }
    }

    /// <summary>Reclaims retired GPU resources whose timelines have completed.</summary>
    public void Reclaim()
    {
        EnsureNotDestroyed();
        _system?.Tick();
    }

    /// <summary>
    /// Updates the graph's viewport extent, retires old viewport-relative pooled textures safely behind their GPU timelines,
    /// and notifies all nodes.
    /// </summary>
    /// <param name="viewport">The new viewport extent in pixels; zero dimensions are ignored.</param>
    public void Resize(Extent2D viewport)
    {
        EnsureMutable();
        if (viewport.X == 0 || viewport.Y == 0)
            return;
        if (viewport.X == _viewport.X && viewport.Y == _viewport.Y)
            return;
        _viewport = viewport;
        _pools.RetireViewportRelativeTextures(this);
        for (int index = 0; index < _nodes.Count; ++index)
            _nodes[index].Resize(viewport);
    }

    /// <summary>
    /// Disposes all graph-owned pooled resources and native profiling storage.
    /// Does not dispose caller-owned nodes or imported resources.
    /// </summary>
    public void Dispose()
    {
        if (_destroyed)
            return;
        Debug.Assert(!_inSetup && !_inExecute);
        _destroyed = true;
        _lastContext = null;
        _lastContextDiscardVersion = 0;
        _pools.Destroy();
        if (_timestampSlots != null)
        {
            for (int index = 0; index < _timestampSlots.Length; ++index)
            {
                ref TimestampSlot slot = ref _timestampSlots[index];
                if (slot.Context != null && !slot.Context.IsDisposed && slot.Context.IsFramePending(slot.ContextFrameId))
                    slot.Context.Wait();
                slot.Context = null;
                slot.ContextFrameId = 0;
                if (slot.Values != null)
                {
                    NativeMemory.Free(slot.Values);
                    slot.Values = null;
                }
            }
            _timestampSlots = null;
        }
        Array.Clear(_resources, 0, _resources.Length);
        Array.Clear(_passes, 0, _passes.Length);
        _nodes.Clear();
    }

    /// <summary>Legacy alias for <see cref="Dispose"/>.</summary>
    [Obsolete("Use Dispose() instead.")]
    public void Destroy() => Dispose();

    // Internal dry-run helpers for CPU unit tests.
    internal RGTexture ImportDryRunTexture(object token, Extent3D extent, Format format, TextureUsage usage, string name,
        RGUsage finalUsage = RGUsage.None)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(token);
        ValidateFinalUsage(finalUsage, isTexture: true, usage, name);
        int index = AllocateRetainedSlot();
        ushort generation = NextGeneration(_resources[index].Generation);
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = true,
            IsTexture = true,
            Kind = ResourceKind.ImportedTexture,
            Generation = generation,
            Name = name,
            TextureDesc = new RGTextureDesc { Name = name, Extent = extent, Format = format, Usage = usage },
            ExternalKey = token,
            FinalUsage = finalUsage,
            StickyPoolIndex = -1,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGTexture((ushort)index, generation);
    }

    internal RGBuffer ImportDryRunBuffer(object token, ulong sizeBytes, string name, RGUsage finalUsage = RGUsage.None)
    {
        EnsureMutable();
        ArgumentNullException.ThrowIfNull(token);
        ValidateFinalUsage(finalUsage, isTexture: false, TextureUsage.None, name);
        int index = AllocateRetainedSlot();
        ushort generation = NextGeneration(_resources[index].Generation);
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = true,
            IsTexture = false,
            Kind = ResourceKind.ImportedBuffer,
            Generation = generation,
            Name = name,
            BufferSizeBytes = sizeBytes,
            ExternalKey = token,
            FinalUsage = finalUsage,
            StickyPoolIndex = -1,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGBuffer((ushort)index, generation);
    }

    internal int CompileDryRun(RenderGraphTarget output = default, RGUsage outputFinalUsage = RGUsage.None)
    {
        EnsureNotDestroyed();
        try
        {
            CompileFrame(output, outputFinalUsage, dryRun: true);
            FinalizeDryRunReports();
            return _statistics.AlivePassCount;
        }
        finally
        {
            ClearFrameSlotReferences();
        }
    }

    internal int GetPhysicalIndex(RGTexture handle)
    {
        int index = ValidateTextureHandle(handle);
        return _resources[index].Physical.IsValid ? _resources[index].Physical.Index : -1;
    }

    internal int GetPhysicalIndex(RGBuffer handle)
    {
        int index = ValidateBufferHandle(handle);
        return _resources[index].Physical.IsValid ? _resources[index].Physical.Index : -1;
    }

    internal TextureUsage GetResolvedTextureUsage(RGTexture handle)
    {
        int index = ValidateTextureHandle(handle);
        ref ResourceSlot slot = ref _resources[index];
        return slot.ExplicitUsage != TextureUsage.None ? slot.ExplicitUsage : slot.InferredUsage;
    }

    internal void OnPooledTextureRetired(int removedIndex, int movedFromIndex)
    {
        for (int index = 1; index < _resourceCount; ++index)
        {
            ref ResourceSlot slot = ref _resources[index];
            if (!slot.Active || slot.Kind != ResourceKind.TransientTexture)
                continue;
            if (slot.IsRetained)
            {
                if (slot.StickyPoolIndex == removedIndex)
                    slot.StickyPoolIndex = -1;
                else if (movedFromIndex != removedIndex && slot.StickyPoolIndex == movedFromIndex)
                    slot.StickyPoolIndex = removedIndex;
            }
            if (slot.Physical.Kind == PhysicalKind.PooledTexture)
            {
                if (slot.Physical.Index == removedIndex)
                    slot.Physical = default;
                else if (movedFromIndex != removedIndex && slot.Physical.Index == movedFromIndex)
                    slot.Physical = new PhysicalRef(PhysicalKind.PooledTexture, removedIndex);
            }
        }
    }

    internal void OnPooledBufferRetired(int removedIndex, int movedFromIndex)
    {
        for (int index = 1; index < _resourceCount; ++index)
        {
            ref ResourceSlot slot = ref _resources[index];
            if (!slot.Active || slot.Kind != ResourceKind.TransientBuffer)
                continue;
            if (slot.IsRetained)
            {
                if (slot.StickyPoolIndex == removedIndex)
                    slot.StickyPoolIndex = -1;
                else if (movedFromIndex != removedIndex && slot.StickyPoolIndex == movedFromIndex)
                    slot.StickyPoolIndex = removedIndex;
            }
            if (slot.Physical.Kind == PhysicalKind.PooledBuffer)
            {
                if (slot.Physical.Index == removedIndex)
                    slot.Physical = default;
                else if (movedFromIndex != removedIndex && slot.Physical.Index == movedFromIndex)
                    slot.Physical = new PhysicalRef(PhysicalKind.PooledBuffer, removedIndex);
            }
        }
    }

    internal void OnExternalRetired(int removedIndex, int movedFromIndex)
    {
        for (int index = 0; index < _resourceCount; ++index)
        {
            ref ResourceSlot slot = ref _resources[index];
            if (!slot.Active || slot.Physical.Kind != PhysicalKind.External)
                continue;
            if (slot.Physical.Index == removedIndex)
                slot.Physical = default;
            else if (movedFromIndex != removedIndex && slot.Physical.Index == movedFromIndex)
                slot.Physical = new PhysicalRef(PhysicalKind.External, removedIndex);
        }
    }

    // Internal builder hooks.
    internal RGTexture AddFrameTransientTexture(in RGTextureDesc desc)
    {
        Debug.Assert(_inSetup);
        ValidateTransientTextureDesc(in desc);
        int index = AllocateFrameSlot();
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = false,
            IsTexture = true,
            Kind = ResourceKind.TransientTexture,
            Generation = _frameGeneration,
            Name = string.IsNullOrEmpty(desc.Name) ? "frame_texture" : desc.Name,
            TextureDesc = desc,
            StickyPoolIndex = -1,
            ResolvedExtent = desc.ResolveExtent(_viewport),
            Format = desc.Format,
            ExplicitUsage = desc.Usage,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGTexture((ushort)index, _frameGeneration);
    }

    internal RGBuffer AddFrameTransientBuffer(in RGBufferDesc desc)
    {
        Debug.Assert(_inSetup);
        ValidateTransientBufferDesc(in desc);
        int index = AllocateFrameSlot();
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = false,
            IsTexture = false,
            Kind = ResourceKind.TransientBuffer,
            Generation = _frameGeneration,
            Name = string.IsNullOrEmpty(desc.Name) ? "frame_buffer" : desc.Name,
            BufferSizeBytes = desc.SizeBytes,
            StickyPoolIndex = -1,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGBuffer((ushort)index, _frameGeneration);
    }

    internal RGTexture AddFrameImportedTexture(Texture texture, string name, RGUsage finalUsage)
    {
        Debug.Assert(_inSetup);
        ArgumentNullException.ThrowIfNull(texture);
        Debug.Assert(!texture.IsDisposed);
        string resolvedName = string.IsNullOrEmpty(name) ? "frame_imported_texture" : name;
        ValidateFinalUsage(finalUsage, isTexture: true, texture.Desc.Usage, resolvedName);
        int existing = FindImportedSlot(texture, isTexture: true);
        if (existing >= 0)
        {
            _resources[existing].FrameFinalUsage |= finalUsage;
            return new RGTexture((ushort)existing, _resources[existing].Generation);
        }
        int index = AllocateFrameSlot();
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = false,
            IsTexture = true,
            Kind = ResourceKind.ImportedTexture,
            Generation = _frameGeneration,
            Name = resolvedName,
            ExternalTexture = texture,
            ExternalKey = texture,
            FinalUsage = finalUsage,
            FrameFinalUsage = finalUsage,
            StickyPoolIndex = -1,
            ResolvedExtent = texture.Extent,
            Format = texture.Format,
            ExplicitUsage = texture.Desc.Usage,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGTexture((ushort)index, _frameGeneration);
    }

    internal RGTexture AddFrameDryRunImportedTexture(object token, Extent3D extent, Format format, TextureUsage usage, string name, RGUsage finalUsage)
    {
        Debug.Assert(_inSetup);
        ArgumentNullException.ThrowIfNull(token);
        string resolvedName = string.IsNullOrEmpty(name) ? "frame_imported_texture" : name;
        ValidateFinalUsage(finalUsage, isTexture: true, usage, resolvedName);
        int existing = FindImportedSlot(token, isTexture: true);
        if (existing >= 0)
        {
            _resources[existing].FrameFinalUsage |= finalUsage;
            return new RGTexture((ushort)existing, _resources[existing].Generation);
        }
        int index = AllocateFrameSlot();
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = false,
            IsTexture = true,
            Kind = ResourceKind.ImportedTexture,
            Generation = _frameGeneration,
            Name = resolvedName,
            ExternalKey = token,
            FinalUsage = finalUsage,
            FrameFinalUsage = finalUsage,
            StickyPoolIndex = -1,
            ResolvedExtent = extent,
            Format = format,
            ExplicitUsage = usage,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGTexture((ushort)index, _frameGeneration);
    }

    internal RGBuffer AddFrameImportedBuffer(Buffer buffer, string name, RGUsage finalUsage)
    {
        Debug.Assert(_inSetup);
        ArgumentNullException.ThrowIfNull(buffer);
        Debug.Assert(!buffer.IsDisposed);
        string resolvedName = string.IsNullOrEmpty(name) ? "frame_imported_buffer" : name;
        ValidateFinalUsage(finalUsage, isTexture: false, TextureUsage.None, resolvedName);
        int existing = FindImportedSlot(buffer, isTexture: false);
        if (existing >= 0)
        {
            _resources[existing].FrameFinalUsage |= finalUsage;
            return new RGBuffer((ushort)existing, _resources[existing].Generation);
        }
        int index = AllocateFrameSlot();
        _resources[index] = new ResourceSlot
        {
            Active = true,
            IsRetained = false,
            IsTexture = false,
            Kind = ResourceKind.ImportedBuffer,
            Generation = _frameGeneration,
            Name = resolvedName,
            BufferSizeBytes = buffer.SizeBytes,
            ExternalBuffer = buffer,
            ExternalKey = buffer,
            FinalUsage = finalUsage,
            FrameFinalUsage = finalUsage,
            StickyPoolIndex = -1,
            FirstPass = -1,
            LastPass = -1,
        };
        return new RGBuffer((ushort)index, _frameGeneration);
    }

    private int FindImportedSlot(object key, bool isTexture)
    {
        for (int index = 0; index < _resourceCount; ++index)
        {
            ref ResourceSlot slot = ref _resources[index];
            if (slot.Active && slot.IsTexture == isTexture && ReferenceEquals(slot.ExternalKey, key))
                return index;
        }
        return -1;
    }

    internal Extent3D GetTextureExtent(RGTexture handle)
    {
        int index = ValidateTextureHandle(handle);
        return _resources[index].ResolvedExtent;
    }

    internal Format GetTextureFormat(RGTexture handle)
    {
        int index = ValidateTextureHandle(handle);
        return _resources[index].Format;
    }

    internal bool SupportsTextureUsage(RGTexture handle, RGUsage usage)
    {
        int index = ValidateTextureHandle(handle);
        ref ResourceSlot slot = ref _resources[index];
        if (usage == RGUsage.None)
            return true;
        if (slot.ExplicitUsage != TextureUsage.None && !RGUsageInfo.IsTextureUsageCompatible(slot.ExplicitUsage, usage))
            return false;
        if (_system == null)
            return true;
        TextureUsage required = RGUsageInfo.RequiredTextureUsage(usage);
        return required == TextureUsage.None || _system.Device.SupportsTextureFormat(slot.Format, required);
    }

    internal ulong GetBufferSizeBytes(RGBuffer handle)
    {
        int index = ValidateBufferHandle(handle);
        return _resources[index].BufferSizeBytes;
    }

    internal int AddPassRecord(string name, IRenderGraphNode node, int nodeIndex, int passId)
    {
        Debug.Assert(_inSetup);
        if (_passCount == _passes.Length)
            Array.Resize(ref _passes, _passes.Length * 2);
        int passIndex = _passCount++;
        _passes[passIndex] = new PassRecord
        {
            Name = name,
            Node = node,
            NodeIndex = nodeIndex,
            PassId = passId,
            FirstAccess = -1,
            LastAccess = -1,
        };
        return passIndex;
    }

    internal void AddPassTextureAccess(int passIndex, RGTexture handle, RGUsage usage, bool isLogicalRead, bool isLogicalWrite)
    {
        Debug.Assert(_inSetup && passIndex >= 0 && passIndex < _passCount);
        int resourceIndex = ValidateTextureHandle(handle);
        ValidatePassUsage(usage, isTexture: true, isLogicalRead, isLogicalWrite, _passes[passIndex].Name, _resources[resourceIndex].Name);
        AddOrMergePassAccess(passIndex, resourceIndex, usage, isLogicalRead, isLogicalWrite);
    }

    internal void AddPassBufferAccess(int passIndex, RGBuffer handle, RGUsage usage, bool isLogicalRead, bool isLogicalWrite)
    {
        Debug.Assert(_inSetup && passIndex >= 0 && passIndex < _passCount);
        int resourceIndex = ValidateBufferHandle(handle);
        ValidatePassUsage(usage, isTexture: false, isLogicalRead, isLogicalWrite, _passes[passIndex].Name, _resources[resourceIndex].Name);
        AddOrMergePassAccess(passIndex, resourceIndex, usage, isLogicalRead, isLogicalWrite);
    }

    internal void MarkPassProducesOutput(int passIndex)
    {
        Debug.Assert(_inSetup && passIndex >= 0 && passIndex < _passCount);
        _passes[passIndex].ProducesOutput = true;
    }

    // Internal execution context resolvers.
    internal Texture ResolveTexture(RGTexture handle)
    {
        Texture? texture = TryResolveTexture(handle);
        if (texture == null)
            throw new InvalidOperationException($"RenderGraph '{Name}': texture handle {handle} cannot be resolved to a Texture.");
        return texture;
    }

    internal Texture? TryResolveTexture(RGTexture handle)
    {
        if (!TryValidateTextureHandle(handle, out int index))
            return null;
        ref ResourceSlot slot = ref _resources[index];
        return slot.Kind switch
        {
            ResourceKind.ImportedTexture => slot.ExternalTexture,
            ResourceKind.TransientTexture => slot.Physical.Kind == PhysicalKind.PooledTexture
                ? _pools.GetPooledTexture(slot.Physical.Index)
                : null,
            _ => null,
        };
    }

    internal RenderTarget ResolveRenderTarget(RGTexture handle)
    {
        int index = ValidateTextureHandle(handle);
        ref ResourceSlot slot = ref _resources[index];
        if (slot.Kind == ResourceKind.SwapchainOutput)
        {
            if (!_currentOutput.IsValid)
                throw new InvalidOperationException($"RenderGraph '{Name}': output has no render target.");
            return _currentOutput.Target;
        }
        Texture texture = ResolveTexture(handle);
        return texture.GetRenderTarget();
    }

    internal GPURenderView ResolveRenderView(RGTexture handle)
    {
        int index = ValidateTextureHandle(handle);
        ref ResourceSlot slot = ref _resources[index];
        if (slot.Kind == ResourceKind.SwapchainOutput)
            return _currentOutput.RenderView ?? throw new InvalidOperationException($"RenderGraph '{Name}': output has no render view.");
        Texture texture = ResolveTexture(handle);
        return texture.LowLevelRenderView;
    }

    internal GPUTexture ResolveNativeTexture(RGTexture handle)
    {
        int index = ValidateTextureHandle(handle);
        ref ResourceSlot slot = ref _resources[index];
        if (slot.Kind == ResourceKind.SwapchainOutput)
            return _currentOutput.NativeTexture ?? throw new InvalidOperationException($"RenderGraph '{Name}': output has no native texture.");
        Texture texture = ResolveTexture(handle);
        return texture.LowLevelTexture;
    }

    internal Buffer ResolveBuffer(RGBuffer handle)
    {
        Buffer? buffer = TryResolveBuffer(handle);
        if (buffer == null)
            throw new InvalidOperationException($"RenderGraph '{Name}': buffer handle {handle} cannot be resolved to a Buffer.");
        return buffer;
    }

    internal Buffer? TryResolveBuffer(RGBuffer handle)
    {
        if (!TryValidateBufferHandle(handle, out int index))
            return null;
        ref ResourceSlot slot = ref _resources[index];
        return slot.Kind switch
        {
            ResourceKind.ImportedBuffer => slot.ExternalBuffer,
            ResourceKind.TransientBuffer => slot.Physical.Kind == PhysicalKind.PooledBuffer
                ? _pools.GetPooledBuffer(slot.Physical.Index)
                : null,
            _ => null,
        };
    }

    // Core compilation pipeline: Setup -> Validate -> Cull -> Infer & Lifetimes -> Allocate -> Plan Barriers.
    private void CompileFrame(RenderGraphTarget output, RGUsage outputFinalUsage, bool dryRun, in FrameUse frameUse = default)
    {
        ++_frameNumber;
        _currentOutput = output;
        HasOutputThisFrame = output.IsValid || (dryRun && output.IsDryRunTarget);
        _frameResourceBase = _retainedHighWater;
        _resourceCount = _retainedHighWater;
        _frameGeneration = NextGeneration(_frameGeneration);
        _passCount = 0;
        _accessCount = 0;
        _exitBarrier = default;

        PrepareOutputSlot(output, outputFinalUsage);
        ResetRetainedSlotsForFrame();

        // Phase 1: Setup.
        _inSetup = true;
        try
        {
            for (int nodeIndex = 0; nodeIndex < _nodes.Count; ++nodeIndex)
            {
                IRenderGraphNode node = _nodes[nodeIndex];
                if (!node.IsEnabled)
                    continue;
                _builder.Attach(this, node, nodeIndex);
                node.Setup(_builder);
            }
        }
        finally
        {
            _builder.Detach();
            _inSetup = false;
        }

        // Phase 2: Validate all declared passes before mutating pools or sync states.
        ValidatePasses();

        // Phase 3: Dead-pass culling from outputs and external handoffs.
        int alivePassCount = CullPasses();

        // Phase 4: Infer minimal transient TextureUsage and compute pass lifetimes.
        ComputeLifetimesAndUsage();

        // Phase 5: Lifetime-aliased physical pooling and idle eviction.
        AllocatePhysicalResources(dryRun, in frameUse, out int logicalTextures, out int logicalBuffers);

        // Phase 6: Plan explicit per-pass and trailing exit barriers.
        int emittedBarriers = PlanBarriers();

        _statistics = new RenderGraphStatistics
        {
            TotalPassCount = _passCount,
            AlivePassCount = alivePassCount,
            CulledPassCount = _passCount - alivePassCount,
            EmittedBarrierCount = emittedBarriers,
            LogicalTransientTextureCount = logicalTextures,
            ActivePhysicalTextureCount = _pools.ActivePhysicalTextureCount,
            TotalPooledTextureCount = _pools.TotalPooledTextureCount,
            LogicalTransientBufferCount = logicalBuffers,
            ActivePhysicalBufferCount = _pools.ActivePhysicalBufferCount,
            TotalPooledBufferCount = _pools.TotalPooledBufferCount,
            RetiredResourceCount = _pools.RetiredResourceCount,
        };
    }

    private void PrepareOutputSlot(RenderGraphTarget output, RGUsage outputFinalUsage)
    {
        if (!HasOutputThisFrame)
        {
            _resources[0] = default;
            return;
        }

        TextureUsage outputUsage = output.Texture != null
            ? output.Texture.Desc.Usage
            : TextureUsage.ColorAttachment | TextureUsage.TransferSource | TextureUsage.TransferDestination;
        ValidateFinalUsage(outputFinalUsage, isTexture: true, outputUsage, "Output");

        _outputGeneration = NextGeneration(_outputGeneration);
        _resources[0] = new ResourceSlot
        {
            Active = true,
            IsRetained = false,
            IsTexture = true,
            Kind = output.Texture != null ? ResourceKind.ImportedTexture : ResourceKind.SwapchainOutput,
            Generation = _outputGeneration,
            Name = "Output",
            ExternalTexture = output.Texture,
            ExternalKey = output.Texture,
            FinalUsage = outputFinalUsage,
            FrameFinalUsage = outputFinalUsage,
            StickyPoolIndex = -1,
            ResolvedExtent = new Extent3D(output.Extent.X, output.Extent.Y, 1),
            Format = output.Format,
            ExplicitUsage = outputUsage,
            FirstPass = -1,
            LastPass = -1,
        };
    }

    private void ResetRetainedSlotsForFrame()
    {
        for (int index = 1; index < _retainedHighWater; ++index)
        {
            ref ResourceSlot slot = ref _resources[index];
            if (!slot.Active)
                continue;
            slot.HasWriter = false;
            slot.Needed = false;
            slot.TouchedByAlivePass = false;
            slot.FirstPass = -1;
            slot.LastPass = -1;
            slot.InferredUsage = TextureUsage.None;
            slot.FrameFinalUsage = slot.FinalUsage;
            slot.Physical = default;

            switch (slot.Kind)
            {
                case ResourceKind.TransientTexture:
                    slot.ResolvedExtent = slot.TextureDesc.ResolveExtent(_viewport);
                    slot.Format = slot.TextureDesc.Format;
                    slot.ExplicitUsage = slot.TextureDesc.Usage;
                    break;
                case ResourceKind.ImportedTexture:
                    if (slot.ExternalTexture != null)
                    {
                        Debug.Assert(!slot.ExternalTexture.IsDisposed);
                        slot.ResolvedExtent = slot.ExternalTexture.Extent;
                        slot.Format = slot.ExternalTexture.Format;
                        slot.ExplicitUsage = slot.ExternalTexture.Desc.Usage;
                    }
                    else
                    {
                        // Dry-run imported texture stores metadata in TextureDesc.
                        slot.ResolvedExtent = slot.TextureDesc.Extent;
                        slot.Format = slot.TextureDesc.Format;
                        slot.ExplicitUsage = slot.TextureDesc.Usage;
                    }
                    break;
                case ResourceKind.ImportedBuffer:
                    if (slot.ExternalBuffer != null)
                    {
                        Debug.Assert(!slot.ExternalBuffer.IsDisposed);
                        slot.BufferSizeBytes = slot.ExternalBuffer.SizeBytes;
                    }
                    break;
            }
        }
    }

    private void ValidatePasses()
    {
        for (int passIndex = 0; passIndex < _passCount; ++passIndex)
        {
            ref PassRecord pass = ref _passes[passIndex];
            int reads = 0;
            int writes = 0;
            for (int accessIndex = pass.FirstAccess; accessIndex != -1; accessIndex = _accesses[accessIndex].NextAccess)
            {
                PassAccess access = _accesses[accessIndex];
                ref ResourceSlot slot = ref _resources[access.ResourceIndex];
                if (access.IsLogicalRead)
                    ++reads;
                if (access.IsLogicalWrite)
                    ++writes;

                if (slot.IsTexture)
                {
                    if (RGUsageInfo.HasTextureSamePassConflict(access.Usage))
                    {
                        throw new InvalidOperationException(
                            $"RenderGraph '{Name}': pass '{pass.Name}' has conflicting simultaneous accesses ({access.Usage}) on texture '{slot.Name}'.");
                    }
                    if (slot.ExplicitUsage != TextureUsage.None && !RGUsageInfo.IsTextureUsageCompatible(slot.ExplicitUsage, access.Usage))
                    {
                        throw new InvalidOperationException(
                            $"RenderGraph '{Name}': pass '{pass.Name}' requires {access.Usage} on texture '{slot.Name}', " +
                            $"incompatible with TextureUsage ({slot.ExplicitUsage}).");
                    }
                }

                if (slot.Kind is ResourceKind.TransientTexture or ResourceKind.TransientBuffer)
                {
                    if (access.IsLogicalRead && !slot.HasWriter)
                    {
                        throw new InvalidOperationException(
                            $"RenderGraph '{Name}': pass '{pass.Name}' reads transient resource '{slot.Name}' before any earlier pass writes it.");
                    }
                    if (access.IsLogicalWrite)
                        slot.HasWriter = true;
                }
            }
            pass.ReadCount = reads;
            pass.WriteCount = writes;
        }
    }

    private int CullPasses()
    {
        if (HasOutputThisFrame)
            _resources[0].Needed = true;
        for (int index = 1; index < _resourceCount; ++index)
        {
            if (_resources[index].Active && _resources[index].FrameFinalUsage != RGUsage.None)
                _resources[index].Needed = true;
        }

        int aliveCount = 0;
        for (int passIndex = _passCount - 1; passIndex >= 0; --passIndex)
        {
            ref PassRecord pass = ref _passes[passIndex];
            bool alive = pass.ProducesOutput;
            if (!alive)
            {
                for (int accessIndex = pass.FirstAccess; accessIndex != -1; accessIndex = _accesses[accessIndex].NextAccess)
                {
                    PassAccess access = _accesses[accessIndex];
                    if (access.IsLogicalWrite && _resources[access.ResourceIndex].Needed)
                    {
                        alive = true;
                        break;
                    }
                }
            }

            pass.Alive = alive;
            if (!alive)
                continue;
            ++aliveCount;

            for (int accessIndex = pass.FirstAccess; accessIndex != -1; accessIndex = _accesses[accessIndex].NextAccess)
            {
                PassAccess access = _accesses[accessIndex];
                if (access.IsLogicalRead)
                    _resources[access.ResourceIndex].Needed = true;
                else if (access.IsLogicalWrite)
                    _resources[access.ResourceIndex].Needed = false;
            }
        }
        return aliveCount;
    }

    private void ComputeLifetimesAndUsage()
    {
        for (int passIndex = 0; passIndex < _passCount; ++passIndex)
        {
            if (!_passes[passIndex].Alive)
                continue;
            for (int accessIndex = _passes[passIndex].FirstAccess; accessIndex != -1; accessIndex = _accesses[accessIndex].NextAccess)
            {
                PassAccess access = _accesses[accessIndex];
                ref ResourceSlot slot = ref _resources[access.ResourceIndex];
                slot.TouchedByAlivePass = true;
                if (slot.FirstPass == -1)
                    slot.FirstPass = passIndex;
                slot.LastPass = passIndex;
                if (slot.IsTexture && slot.ExplicitUsage == TextureUsage.None)
                    slot.InferredUsage |= RGUsageInfo.RequiredTextureUsage(access.Usage);
            }
        }
    }

    private void AllocatePhysicalResources(bool dryRun, in FrameUse frameUse, out int logicalTextures, out int logicalBuffers)
    {
        _pools.BeginFrame();
        _allocCount = 0;
        _activeTransientCount = 0;
        logicalTextures = 0;
        logicalBuffers = 0;

        for (int index = 1; index < _resourceCount; ++index)
        {
            ref ResourceSlot slot = ref _resources[index];
            if (!slot.Active || !slot.TouchedByAlivePass)
                continue;
            if (slot.Kind is ResourceKind.TransientTexture or ResourceKind.TransientBuffer)
            {
                if (_allocCount == _allocOrder.Length)
                    Array.Resize(ref _allocOrder, _allocOrder.Length * 2);
                _allocOrder[_allocCount++] = index;
            }
        }

        // Insertion sort by (FirstPass, ResourceIndex) for deterministic, zero-allocation lifetime scanning.
        for (int index = 1; index < _allocCount; ++index)
        {
            int keyIndex = _allocOrder[index];
            int keyFirst = _resources[keyIndex].FirstPass;
            int position = index - 1;
            while (position >= 0)
            {
                int priorIndex = _allocOrder[position];
                int priorFirst = _resources[priorIndex].FirstPass;
                if (priorFirst < keyFirst || (priorFirst == keyFirst && priorIndex < keyIndex))
                    break;
                _allocOrder[position + 1] = priorIndex;
                --position;
            }
            _allocOrder[position + 1] = keyIndex;
        }

        for (int orderIndex = 0; orderIndex < _allocCount; ++orderIndex)
        {
            int resourceIndex = _allocOrder[orderIndex];
            int currentFirst = _resources[resourceIndex].FirstPass;

            // Release any active transient whose final pass strictly precedes currentFirst.
            int keep = 0;
            for (int activeIndex = 0; activeIndex < _activeTransientCount; ++activeIndex)
            {
                int candidate = _activeTransients[activeIndex];
                if (_resources[candidate].LastPass < currentFirst)
                {
                    if (_resources[candidate].Kind == ResourceKind.TransientTexture)
                        _pools.ReleaseTextureForReuse(_resources[candidate].Physical.Index);
                    else
                        _pools.ReleaseBufferForReuse(_resources[candidate].Physical.Index);
                }
                else
                {
                    _activeTransients[keep++] = candidate;
                }
            }
            _activeTransientCount = keep;

            ref ResourceSlot slot = ref _resources[resourceIndex];
            if (slot.Kind == ResourceKind.TransientTexture)
            {
                TextureUsage resolvedUsage = slot.ExplicitUsage != TextureUsage.None ? slot.ExplicitUsage : slot.InferredUsage;
                TextureDesc key = slot.TextureDesc.ResolveTextureDesc(_viewport, resolvedUsage);
                bool viewportRelative = slot.TextureDesc.Extent.X == 0 || slot.TextureDesc.Extent.Y == 0;
                int sticky = slot.IsRetained ? slot.StickyPoolIndex : -1;
                int poolIndex = _pools.AllocateTexture(in key, sticky, viewportRelative, _frameNumber, dryRun, in frameUse);
                slot.Physical = new PhysicalRef(PhysicalKind.PooledTexture, poolIndex);
                if (slot.IsRetained)
                    slot.StickyPoolIndex = poolIndex;
                ++logicalTextures;
            }
            else
            {
                int sticky = slot.IsRetained ? slot.StickyPoolIndex : -1;
                int poolIndex = _pools.AllocateBuffer(slot.BufferSizeBytes, sticky, _frameNumber, dryRun, in frameUse);
                slot.Physical = new PhysicalRef(PhysicalKind.PooledBuffer, poolIndex);
                if (slot.IsRetained)
                    slot.StickyPoolIndex = poolIndex;
                ++logicalBuffers;
            }

            if (_activeTransientCount == _activeTransients.Length)
                Array.Resize(ref _activeTransients, _activeTransients.Length * 2);
            _activeTransients[_activeTransientCount++] = resourceIndex;
        }

        // Bind physical sync references for output and imported resources touched by alive passes.
        for (int index = 0; index < _resourceCount; ++index)
        {
            ref ResourceSlot slot = ref _resources[index];
            if (!slot.Active || !slot.TouchedByAlivePass)
                continue;
            if (slot.Kind == ResourceKind.SwapchainOutput)
                slot.Physical = new PhysicalRef(PhysicalKind.SwapchainOutput, 0);
            else if (slot.Kind is ResourceKind.ImportedTexture or ResourceKind.ImportedBuffer)
                slot.Physical = new PhysicalRef(PhysicalKind.External, _pools.GetOrAddExternal(slot.ExternalKey!, _frameNumber, in frameUse));
        }

        _pools.EvictIdleResources(_frameNumber, IdleFrameRetireThreshold, this);
    }

    private int PlanBarriers()
    {
        int emittedBarriers = 0;

        for (int passIndex = 0; passIndex < _passCount; ++passIndex)
        {
            ref PassRecord pass = ref _passes[passIndex];
            pass.Barrier = default;
            if (!pass.Alive)
                continue;

            Stage before = Stage.None;
            Access beforeAccess = Access.None;
            Stage after = Stage.None;
            Access afterAccess = Access.None;

            for (int accessIndex = pass.FirstAccess; accessIndex != -1; accessIndex = _accesses[accessIndex].NextAccess)
            {
                PassAccess access = _accesses[accessIndex];
                ref PhysicalSyncState sync = ref _pools.GetSyncState(_resources[access.ResourceIndex].Physical);
                AccumulateHazardBarrier(in sync, access.Usage, access.IsLogicalRead, access.IsLogicalWrite,
                    ref before, ref beforeAccess, ref after, ref afterAccess);
            }

            if (before != Stage.None && after != Stage.None)
            {
                pass.Barrier = new RGBarrier
                {
                    Before = before,
                    BeforeAccess = beforeAccess,
                    After = after,
                    AfterAccess = afterAccess,
                };
                ++emittedBarriers;
                _pools.PropagateGlobalBarrier(before, beforeAccess, after, afterAccess);
            }

            for (int accessIndex = pass.FirstAccess; accessIndex != -1; accessIndex = _accesses[accessIndex].NextAccess)
            {
                PassAccess access = _accesses[accessIndex];
                ref PhysicalSyncState sync = ref _pools.GetSyncState(_resources[access.ResourceIndex].Physical);
                ApplyAccessToSync(ref sync, access.Usage, access.IsLogicalRead, access.IsLogicalWrite);
            }
        }

        // Plan trailing exit barrier for touched output/imported resources with external handoff usage.
        Stage exitBefore = Stage.None;
        Access exitBeforeAccess = Access.None;
        Stage exitAfter = Stage.None;
        Access exitAfterAccess = Access.None;

        for (int index = 0; index < _resourceCount; ++index)
        {
            ref ResourceSlot slot = ref _resources[index];
            if (!slot.Active || !slot.TouchedByAlivePass || slot.FrameFinalUsage == RGUsage.None)
                continue;
            ref PhysicalSyncState sync = ref _pools.GetSyncState(slot.Physical);
            bool hasRead = RGUsageInfo.HasRead(slot.FrameFinalUsage);
            bool hasWrite = RGUsageInfo.HasWrite(slot.FrameFinalUsage);
            AccumulateHazardBarrier(in sync, slot.FrameFinalUsage, hasRead, hasWrite,
                ref exitBefore, ref exitBeforeAccess, ref exitAfter, ref exitAfterAccess);
        }

        if (exitBefore != Stage.None && exitAfter != Stage.None)
        {
            _exitBarrier = new RGBarrier
            {
                Before = exitBefore,
                BeforeAccess = exitBeforeAccess,
                After = exitAfter,
                AfterAccess = exitAfterAccess,
            };
            ++emittedBarriers;
            _pools.PropagateGlobalBarrier(exitBefore, exitBeforeAccess, exitAfter, exitAfterAccess);
        }

        // Record external post-graph accesses so subsequent frames emit WAR/WAW barriers against them.
        for (int index = 0; index < _resourceCount; ++index)
        {
            ref ResourceSlot slot = ref _resources[index];
            if (!slot.Active || !slot.TouchedByAlivePass || slot.FrameFinalUsage == RGUsage.None)
                continue;
            ref PhysicalSyncState sync = ref _pools.GetSyncState(slot.Physical);
            bool hasRead = RGUsageInfo.HasRead(slot.FrameFinalUsage);
            bool hasWrite = RGUsageInfo.HasWrite(slot.FrameFinalUsage);
            ApplyAccessToSync(ref sync, slot.FrameFinalUsage, hasRead, hasWrite);
        }

        return emittedBarriers;
    }

    private static void AccumulateHazardBarrier(
        in PhysicalSyncState sync,
        RGUsage usage,
        bool isLogicalRead,
        bool isLogicalWrite,
        ref Stage before,
        ref Access beforeAccess,
        ref Stage after,
        ref Access afterAccess)
    {
        RGUsage readBits = RGUsageInfo.ReadBits(usage);
        RGUsage writeBits = RGUsageInfo.WriteBits(usage);

        if (isLogicalRead && readBits != RGUsage.None && sync.HasWrite)
        {
            RGUsage unsyncedReads = readBits & ~sync.SyncedReads;
            if (unsyncedReads != RGUsage.None)
            {
                RGUsageInfo.ToStageAccess(unsyncedReads, out Stage readStage, out Access readAccess);
                before |= sync.WriteStage;
                beforeAccess |= sync.WriteAccess;
                after |= readStage;
                afterAccess |= readAccess;
            }
        }

        if (isLogicalWrite && writeBits != RGUsage.None)
        {
            RGUsageInfo.ToStageAccess(writeBits, out Stage writeStage, out Access writeAccess);
            if (sync.ReadStages != Stage.None)
            {
                before |= sync.ReadStages;
                after |= writeStage;
                // A prior write made available by an earlier read barrier still needs write-domain visibility.
                if (sync.HasWrite)
                    afterAccess |= writeAccess;
            }
            else if (sync.HasWrite)
            {
                before |= sync.WriteStage;
                beforeAccess |= sync.WriteAccess;
                after |= writeStage;
                afterAccess |= writeAccess;
            }
        }
    }

    private static void ApplyAccessToSync(ref PhysicalSyncState sync, RGUsage usage, bool isLogicalRead, bool isLogicalWrite)
    {
        RGUsage writeBits = RGUsageInfo.WriteBits(usage);
        if (isLogicalWrite && writeBits != RGUsage.None)
        {
            RGUsageInfo.ToStageAccess(writeBits, out Stage writeStage, out Access writeAccess);
            sync.HasWrite = true;
            sync.WriteStage = writeStage;
            sync.WriteAccess = writeAccess;
            sync.SyncedReads = RGUsage.None;
            sync.ReadStages = Stage.None;
            return;
        }

        RGUsage readBits = RGUsageInfo.ReadBits(usage);
        if (isLogicalRead && readBits != RGUsage.None)
        {
            RGUsageInfo.ToStageAccess(readBits, out Stage readStage, out Access _);
            sync.ReadStages |= readStage & ~Stage.Host;
        }
    }

    private void ExecuteCompiledPasses(RenderContext context, RenderGraphTarget output, float deltaTime)
    {
        bool profileCpu = ProfilingEnabled;
        int timestampSlotIndex = -1;
        int timestampLimit = 0;
        if (ProfilingEnabled && _system != null)
        {
            CollectCompletedTimestamps();
            if (_system.Device.Caps.TimestampPeriodNs > 0.0f && MaxTimestampPasses > 0)
            {
                int remainingQueries = Math.Max(0, DefaultTimestampQueryBudget - context.TimestampCount);
                int desiredPasses = Math.Min(Math.Min(MaxTimestampPasses, 128), remainingQueries / 2);
                if (desiredPasses > 0)
                {
                    timestampSlotIndex = AcquireTimestampSlot(desiredPasses);
                    if (timestampSlotIndex >= 0)
                        timestampLimit = Math.Min(_timestampSlots![timestampSlotIndex].Capacity, desiredPasses);
                }
            }
        }

        int timedPassCount = 0;
        long totalStartTicks = profileCpu ? Stopwatch.GetTimestamp() : 0;
        double tickToMs = 1000.0 / Stopwatch.Frequency;

        RenderGraphContext graphContext = new(this, context, output, deltaTime);
        _inExecute = true;
        try
        {
            for (int passIndex = 0; passIndex < _passCount; ++passIndex)
            {
                ref PassRecord pass = ref _passes[passIndex];
                pass.CpuMilliseconds = 0.0;
                pass.GpuMilliseconds = profileCpu ? LookupCachedGpuMs(pass.NodeIndex, pass.PassId, pass.Name) : 0.0;
                if (!pass.Alive)
                    continue;

                if (!pass.Barrier.IsEmpty)
                    context.Barrier(pass.Barrier.Before, pass.Barrier.BeforeAccess, pass.Barrier.After, pass.Barrier.AfterAccess);

                context.BeginDebugGroup(pass.Name);
                try
                {
                    bool writeGpuTimestamps = timestampSlotIndex >= 0 && timedPassCount < timestampLimit;
                    int timestampOffset = 0;
                    if (writeGpuTimestamps)
                    {
                        ref TimestampSlot slot = ref _timestampSlots![timestampSlotIndex];
                        timestampOffset = timedPassCount * 2;
                        slot.Values[timestampOffset] = 0;
                        slot.Values[timestampOffset + 1] = 0;
                        slot.NodeIndices![timedPassCount] = pass.NodeIndex;
                        slot.PassIds![timedPassCount] = pass.PassId;
                        slot.PassNames![timedPassCount] = pass.Name;
                        ++timedPassCount;
                        context.WriteTimestamp(new Span<ulong>(slot.Values + timestampOffset, 1), Stage.AllCommands);
                    }

                    long passStartTicks = profileCpu ? Stopwatch.GetTimestamp() : 0;
                    pass.Node.Execute(in graphContext, pass.PassId);
                    if (profileCpu)
                        pass.CpuMilliseconds = (Stopwatch.GetTimestamp() - passStartTicks) * tickToMs;

                    if (writeGpuTimestamps)
                    {
                        ref TimestampSlot slot = ref _timestampSlots![timestampSlotIndex];
                        context.WriteTimestamp(new Span<ulong>(slot.Values + timestampOffset + 1, 1), Stage.AllCommands);
                    }
                }
                finally
                {
                    context.EndDebugGroup();
                }
            }

            if (!_exitBarrier.IsEmpty)
                context.Barrier(_exitBarrier.Before, _exitBarrier.BeforeAccess, _exitBarrier.After, _exitBarrier.AfterAccess);
        }
        finally
        {
            _inExecute = false;
            if (timestampSlotIndex >= 0)
            {
                ref TimestampSlot slot = ref _timestampSlots![timestampSlotIndex];
                slot.Count = timedPassCount;
                slot.Pending = timedPassCount > 0;
                slot.Context = timedPassCount > 0 ? context : null;
                slot.ContextFrameId = timedPassCount > 0 ? context.CurrentFrameId : 0;
            }
        }

        double totalCpuMs = profileCpu ? (Stopwatch.GetTimestamp() - totalStartTicks) * tickToMs : 0.0;
        PopulatePassReports(totalCpuMs);
    }

    private void FinalizeDryRunReports()
    {
        for (int passIndex = 0; passIndex < _passCount; ++passIndex)
        {
            _passes[passIndex].CpuMilliseconds = 0.0;
            _passes[passIndex].GpuMilliseconds = 0.0;
        }
        PopulatePassReports(0.0);
    }

    private void PopulatePassReports(double totalCpuMs)
    {
        if (_passReports.Length < _passCount)
            _passReports = new RenderGraphPassReport[Math.Max(_passCount, _passReports.Length * 2)];
        _passReportCount = _passCount;
        double totalGpuMs = 0.0;
        for (int passIndex = 0; passIndex < _passCount; ++passIndex)
        {
            ref PassRecord pass = ref _passes[passIndex];
            if (pass.Alive)
                totalGpuMs += pass.GpuMilliseconds;
            _passReports[passIndex] = new RenderGraphPassReport
            {
                Name = pass.Name,
                NodeIndex = pass.NodeIndex,
                PassId = pass.PassId,
                Alive = pass.Alive,
                Barrier = pass.Barrier,
                ReadCount = pass.ReadCount,
                WriteCount = pass.WriteCount,
                CpuMilliseconds = pass.CpuMilliseconds,
                GpuMilliseconds = pass.GpuMilliseconds,
            };
        }
        _statistics = new RenderGraphStatistics
        {
            TotalPassCount = _statistics.TotalPassCount,
            AlivePassCount = _statistics.AlivePassCount,
            CulledPassCount = _statistics.CulledPassCount,
            EmittedBarrierCount = _statistics.EmittedBarrierCount,
            LogicalTransientTextureCount = _statistics.LogicalTransientTextureCount,
            ActivePhysicalTextureCount = _statistics.ActivePhysicalTextureCount,
            TotalPooledTextureCount = _statistics.TotalPooledTextureCount,
            LogicalTransientBufferCount = _statistics.LogicalTransientBufferCount,
            ActivePhysicalBufferCount = _statistics.ActivePhysicalBufferCount,
            TotalPooledBufferCount = _statistics.TotalPooledBufferCount,
            RetiredResourceCount = _statistics.RetiredResourceCount,
            TotalCpuMilliseconds = totalCpuMs,
            TotalGpuMilliseconds = totalGpuMs,
        };
    }

    private void CollectCompletedTimestamps()
    {
        if (_timestampSlots == null || _system == null)
            return;
        double periodMs = _system.Device.Caps.TimestampPeriodNs * 1e-6;
        if (periodMs <= 0.0)
            return;

        for (int slotIndex = 0; slotIndex < _timestampSlots.Length; ++slotIndex)
        {
            ref TimestampSlot slot = ref _timestampSlots[slotIndex];
            if (!slot.Pending)
                continue;
            // Wait until RenderContext.Reclaim has called Pool.ReadTimestamps() before reading or reusing slot.Values.
            if (slot.Context != null && slot.Context.IsFramePending(slot.ContextFrameId))
                continue;
            bool hasResolved = slot.Count > 0 && (slot.Values[0] != 0 || slot.Values[1] != 0);
            if (hasResolved)
            {
                for (int index = 0; index < slot.Count; ++index)
                {
                    ulong begin = slot.Values[index * 2];
                    ulong end = slot.Values[index * 2 + 1];
                    double gpuMs = end >= begin ? (end - begin) * periodMs : 0.0;
                    StoreCachedGpuMs(slot.NodeIndices![index], slot.PassIds![index], slot.PassNames![index], gpuMs);
                }
            }
            slot.Pending = false;
            slot.Context = null;
            slot.ContextFrameId = 0;
        }
    }

    private int AcquireTimestampSlot(int desiredCapacity)
    {
        if (_timestampSlots == null)
            _timestampSlots = new TimestampSlot[TimestampSlotCount];

        for (int attempt = 0; attempt < TimestampSlotCount; ++attempt)
        {
            int slotIndex = (_timestampNextSlot + attempt) % TimestampSlotCount;
            ref TimestampSlot slot = ref _timestampSlots[slotIndex];
            if (slot.Pending || (slot.Context != null && slot.Context.IsFramePending(slot.ContextFrameId)))
                continue;
            if (slot.Capacity < desiredCapacity)
            {
                if (slot.Values != null)
                    NativeMemory.Free(slot.Values);
                slot.Values = (ulong*)NativeMemory.AllocZeroed((nuint)(desiredCapacity * 2), (nuint)sizeof(ulong));
                slot.NodeIndices = new int[desiredCapacity];
                slot.PassIds = new int[desiredCapacity];
                slot.PassNames = new string[desiredCapacity];
                slot.Capacity = desiredCapacity;
            }
            _timestampNextSlot = (slotIndex + 1) % TimestampSlotCount;
            return slotIndex;
        }
        return -1;
    }

    private double LookupCachedGpuMs(int nodeIndex, int passId, string name)
    {
        for (int index = 0; index < _cachedTimingCount; ++index)
        {
            if (_cachedTimings[index].NodeIndex == nodeIndex &&
                _cachedTimings[index].PassId == passId &&
                string.Equals(_cachedTimings[index].Name, name, StringComparison.Ordinal))
                return _cachedTimings[index].GpuMilliseconds;
        }
        return 0.0;
    }

    private void StoreCachedGpuMs(int nodeIndex, int passId, string name, double gpuMs)
    {
        for (int index = 0; index < _cachedTimingCount; ++index)
        {
            if (_cachedTimings[index].NodeIndex == nodeIndex &&
                _cachedTimings[index].PassId == passId &&
                string.Equals(_cachedTimings[index].Name, name, StringComparison.Ordinal))
            {
                _cachedTimings[index].GpuMilliseconds = gpuMs;
                return;
            }
        }
        if (_cachedTimingCount == _cachedTimings.Length)
            Array.Resize(ref _cachedTimings, _cachedTimings.Length * 2);
        _cachedTimings[_cachedTimingCount++] = new CachedPassTiming
        {
            NodeIndex = nodeIndex,
            PassId = passId,
            Name = name,
            GpuMilliseconds = gpuMs,
        };
    }

    private void AddOrMergePassAccess(int passIndex, int resourceIndex, RGUsage usage, bool isLogicalRead, bool isLogicalWrite)
    {
        ref PassRecord pass = ref _passes[passIndex];
        for (int index = pass.FirstAccess; index != -1; index = _accesses[index].NextAccess)
        {
            if (_accesses[index].ResourceIndex == resourceIndex)
            {
                _accesses[index].Usage |= usage;
                _accesses[index].IsLogicalRead |= isLogicalRead;
                _accesses[index].IsLogicalWrite |= isLogicalWrite;
                return;
            }
        }

        if (_accessCount == _accesses.Length)
            Array.Resize(ref _accesses, _accesses.Length * 2);
        int created = _accessCount++;
        _accesses[created] = new PassAccess
        {
            ResourceIndex = resourceIndex,
            Usage = usage,
            IsLogicalRead = isLogicalRead,
            IsLogicalWrite = isLogicalWrite,
            NextAccess = -1,
        };
        if (pass.FirstAccess == -1)
            pass.FirstAccess = created;
        else
            _accesses[pass.LastAccess].NextAccess = created;
        pass.LastAccess = created;
    }

    private void ClearFrameSlotReferences()
    {
        // Clear managed object references on frame-scoped slots and pass records while preserving retained slots.
        for (int index = _frameResourceBase; index < _resourceCount; ++index)
        {
            _resources[index].ExternalTexture = null;
            _resources[index].ExternalBuffer = null;
            _resources[index].ExternalKey = null;
            _resources[index].Name = "";
        }
        for (int index = 0; index < _passCount; ++index)
            _passes[index].Node = null!;
        _resources[0].ExternalTexture = null;
        _resources[0].ExternalKey = null;
        _currentOutput = default;
    }

    private int AllocateRetainedSlot()
    {
        for (int index = 1; index < _retainedHighWater; ++index)
        {
            if (!_resources[index].Active)
                return index;
        }
        if (_retainedHighWater >= ushort.MaxValue)
            throw new InvalidOperationException($"RenderGraph '{Name}': resource slot limit exceeded.");
        if (_retainedHighWater == _resources.Length)
            Array.Resize(ref _resources, _resources.Length * 2);
        int slotIndex = _retainedHighWater++;
        if (_resourceCount < _retainedHighWater)
            _resourceCount = _retainedHighWater;
        return slotIndex;
    }

    private int AllocateFrameSlot()
    {
        if (_resourceCount >= ushort.MaxValue)
            throw new InvalidOperationException($"RenderGraph '{Name}': resource slot limit exceeded.");
        if (_resourceCount == _resources.Length)
            Array.Resize(ref _resources, _resources.Length * 2);
        return _resourceCount++;
    }

    private void ReleaseRetainedSlot(int index)
    {
        ushort nextGeneration = NextGeneration(_resources[index].Generation);
        _resources[index] = new ResourceSlot
        {
            Active = false,
            Generation = nextGeneration,
            StickyPoolIndex = -1,
            FirstPass = -1,
            LastPass = -1,
        };
    }

    private int FindNodeIndex<TAnchor>() where TAnchor : class, IRenderGraphNode
    {
        for (int index = 0; index < _nodes.Count; ++index)
        {
            if (_nodes[index] is TAnchor)
                return index;
        }
        return -1;
    }

    private int ValidateTextureHandle(RGTexture handle)
    {
        if (!TryValidateTextureHandle(handle, out int index))
            throw new InvalidOperationException($"RenderGraph '{Name}': invalid or stale texture handle {handle}.");
        return index;
    }

    private bool TryValidateTextureHandle(RGTexture handle, out int index)
    {
        index = handle.Index;
        if (!handle.IsValid || index >= _resourceCount)
            return false;
        ref ResourceSlot slot = ref _resources[index];
        return slot.Active && slot.IsTexture && slot.Generation == handle.Generation;
    }

    private int ValidateBufferHandle(RGBuffer handle)
    {
        if (!TryValidateBufferHandle(handle, out int index))
            throw new InvalidOperationException($"RenderGraph '{Name}': invalid or stale buffer handle {handle}.");
        return index;
    }

    private bool TryValidateBufferHandle(RGBuffer handle, out int index)
    {
        index = handle.Index;
        if (!handle.IsValid || index >= _resourceCount)
            return false;
        ref ResourceSlot slot = ref _resources[index];
        return slot.Active && !slot.IsTexture && slot.Generation == handle.Generation;
    }

    private void ValidateTransientTextureDesc(in RGTextureDesc desc)
    {
        if (desc.Format == Format.Undefined)
            throw new ArgumentException($"RenderGraph '{Name}': transient texture '{desc.Name}' must specify a concrete Format.");
        if (desc.SampleCount is not (0 or 1 or 4))
            throw new ArgumentException($"RenderGraph '{Name}': transient texture '{desc.Name}' sample count must be 1 or 4.");
    }

    private void ValidateTransientBufferDesc(in RGBufferDesc desc)
    {
        if (desc.SizeBytes == 0)
            throw new ArgumentException($"RenderGraph '{Name}': transient buffer '{desc.Name}' must have a positive SizeBytes.");
    }

    private void ValidatePassUsage(RGUsage usage, bool isTexture, bool isLogicalRead, bool isLogicalWrite, string passName, string resourceName)
    {
        if (usage == RGUsage.None)
            throw new ArgumentException($"RenderGraph '{Name}': pass '{passName}' declared RGUsage.None on '{resourceName}'.");
        if ((usage & RGUsage.HostRead) != 0)
            throw new ArgumentException($"RenderGraph '{Name}': pass '{passName}' cannot use RGUsage.HostRead inside a pass (use finalUsage instead).");
        if (isTexture && (usage & RGUsageInfo.BufferOnly) != 0)
            throw new ArgumentException($"RenderGraph '{Name}': pass '{passName}' used buffer-only flags ({usage}) on texture '{resourceName}'.");
        if (!isTexture && (usage & RGUsageInfo.TextureOnly) != 0)
            throw new ArgumentException($"RenderGraph '{Name}': pass '{passName}' used texture-only flags ({usage}) on buffer '{resourceName}'.");
        if (isLogicalRead && !RGUsageInfo.HasRead(usage))
            throw new ArgumentException($"RenderGraph '{Name}': pass '{passName}' declared Read/ReadWrite on '{resourceName}' without read flags ({usage}).");
        if (!isLogicalWrite && RGUsageInfo.HasWrite(usage))
            throw new ArgumentException($"RenderGraph '{Name}': pass '{passName}' declared Read on '{resourceName}' with write flags ({usage}).");
        if (isLogicalWrite && !RGUsageInfo.HasWrite(usage))
            throw new ArgumentException($"RenderGraph '{Name}': pass '{passName}' declared Write/ReadWrite on '{resourceName}' without write flags ({usage}).");
    }

    private void ValidateFinalUsage(RGUsage finalUsage, bool isTexture, TextureUsage availableUsage, string resourceName)
    {
        if (finalUsage == RGUsage.None)
            return;
        if (isTexture && (finalUsage & RGUsageInfo.BufferOnly) != 0)
            throw new ArgumentException($"RenderGraph '{Name}': texture '{resourceName}' finalUsage ({finalUsage}) contains buffer-only flags.");
        if (!isTexture && (finalUsage & RGUsageInfo.TextureOnly) != 0)
            throw new ArgumentException($"RenderGraph '{Name}': buffer '{resourceName}' finalUsage ({finalUsage}) contains texture-only flags.");
        if (isTexture && availableUsage != TextureUsage.None &&
            !RGUsageInfo.IsTextureUsageCompatible(availableUsage, finalUsage & ~RGUsage.HostRead))
        {
            throw new ArgumentException(
                $"RenderGraph '{Name}': texture '{resourceName}' finalUsage ({finalUsage}) is incompatible with TextureUsage ({availableUsage}).");
        }
    }

    private void EnsureNotDestroyed()
    {
        if (_destroyed)
            throw new ObjectDisposedException(nameof(RenderGraph), $"RenderGraph '{Name}' has already been destroyed.");
    }

    private void EnsureMutable()
    {
        EnsureNotDestroyed();
        if (_inSetup || _inExecute)
            throw new InvalidOperationException($"RenderGraph '{Name}': graph structure and retained resources cannot be mutated during Setup or Execute.");
    }

    private static ushort NextGeneration(ushort current)
    {
        ushort next = (ushort)(current + 1);
        return next == 0 ? (ushort)1 : next;
    }
}
