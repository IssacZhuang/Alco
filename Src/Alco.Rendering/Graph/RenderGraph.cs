using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A lightweight render graph: a set of <see cref="IRenderGraphNode"/> nodes that
/// declare per-frame resource reads/writes, plus the transient
/// <see cref="RenderGraphTexture"/> resources flowing between them. Per frame,
/// <see cref="Execute"/> runs four phases:
/// <list type="number">
/// <item><b>Setup</b> — every enabled node declares its dependencies (registration
/// order);</item>
/// <item><b>Compile</b> — dependencies are validated, nodes whose output is unused
/// are culled (transitively from <see cref="RenderGraphBuilder.ProducesOutput"/>
/// roots), and transient lifetimes are computed;</item>
/// <item><b>Assign</b> — a single allocation walk over the used transients (sorted by
/// first touch) hands out pooled textures: assignments whose lifetime ended are
/// released as reuse candidates, so non-overlapping lifetimes alias the same GPU
/// textures;</item>
/// <item><b>Execute</b> — surviving nodes record their GPU work in registration
/// order into the frame-shared <see cref="RenderContext"/>; the shared command buffer
/// is submitted once when the last node completes.</item>
/// </list>
/// Execution order is always registration order — the graph validates it satisfies
/// the declared dependencies instead of reordering, keeping the schedule explicit
/// and predictable.
/// <br/>The allocation walk is deterministic: for an unchanged schedule it reproduces
/// last frame's assignment exactly, so steady-state frames perform no facade
/// rebinding and no managed allocations on the whole Setup/Compile/Execute path.
/// <br/>The graph takes ownership of registered nodes: nodes implementing
/// <see cref="System.IDisposable"/> are disposed with the graph. Pooled textures are
/// disposed with the graph and on <see cref="Resize"/>.
/// </summary>
public sealed class RenderGraph : AutoDisposable
{
    private readonly RenderingSystem _rendering;
    private readonly GPUDevice _device;
    private readonly string _name;
    private readonly List<IRenderGraphNode> _nodes = new();
    private readonly List<RenderGraphNodeRecord> _records = new();
    private readonly List<RenderGraphTexture> _resources = new();
    private readonly RenderGraphTexturePool _pool;
    private readonly RenderGraphCompiler _compiler = new();
    private readonly RenderGraphBuilder _builder = new();
    private readonly RenderGraphContext _context;

    // The frame-shared command buffer: every node's passes are recorded into it and
    // submitted once at the end of Execute (see docs/RenderContext_Refactor.md).
    private readonly RenderContext _sharedContext;

    // Allocation-walk working sets, reused across frames (indices into _resources,
    // sorted by first touch; the still-live assignments with their last touch).
    private readonly List<(RenderGraphTexture Resource, int LastTouch)> _assigned = new();
    private readonly HashSet<object> _keepSet = new();
    private int[] _sortedIndices = new int[16];

    private uint _width;
    private uint _height;
    private bool _inFrame;

    /// <summary>
    /// Creates an empty render graph.
    /// </summary>
    /// <param name="rendering">The rendering system for creating GPU resources.</param>
    /// <param name="width">The initial viewport width in pixels (drives graph-relative transient sizes).</param>
    /// <param name="height">The initial viewport height in pixels.</param>
    /// <param name="name">A diagnostic name for the graph.</param>
    public RenderGraph(RenderingSystem rendering, uint width, uint height, string name = "unnamed_render_graph")
    {
        ArgumentNullException.ThrowIfNull(rendering);
        _rendering = rendering;
        _device = rendering.GraphicsDevice;
        _name = name;
        _width = width;
        _height = height;
        _pool = new RenderGraphTexturePool(CreatePooledAttachment);
        _sharedContext = rendering.CreateRenderContext(name + "_shared");
        _context = new RenderGraphContext(rendering, null, _sharedContext);
    }

    /// <summary>The registered nodes, in execution (registration) order.</summary>
    public IReadOnlyList<IRenderGraphNode> Nodes => _nodes;

    /// <summary>
    /// The frame-shared context every node's passes are recorded into. Exposed so
    /// long-lived consumers (e.g. a canvas whose renderers bind a scope at construction)
    /// can attach to the same context the graph executes with. The graph owns its
    /// lifecycle: never call its internal open/submit APIs or dispose it.
    /// </summary>
    public RenderContext RenderContext => _sharedContext;

    /// <summary>The profiler exposed to nodes through the execution context, or null.
    /// When set, <see cref="Execute"/> brackets the frame with
    /// <see cref="RenderProfiler.BeginFrame"/> / <see cref="RenderProfiler.EndFrame"/>.</summary>
    public RenderProfiler? Profiler { get; set; }

    /// <summary>The current viewport width in pixels.</summary>
    public uint Width => _width;

    /// <summary>The current viewport height in pixels.</summary>
    public uint Height => _height;

    /// <summary>
    /// Whether the frame currently being executed has a final destination (e.g. the
    /// swapchain). Set at the start of <see cref="Execute"/>, before any node's
    /// <see cref="IRenderGraphNode.Setup"/> runs, so nodes can read it from
    /// <see cref="IRenderNode.IsEnabled"/> and <see cref="IRenderGraphNode.Setup"/>
    /// to implement headless-frame behavior (e.g. a final blit disabling itself).
    /// </summary>
    public bool HasDestinationThisFrame { get; private set; } = true;

    /// <summary>The number of textures materialized in the pool (diagnostics).</summary>
    public int PooledTextureCount => _pool.TotalCount;

    /// <summary>
    /// Registers a node at the end of the graph. The graph takes ownership and
    /// disposes the node (when <see cref="System.IDisposable"/>) with itself.
    /// </summary>
    public void Use(IRenderGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ThrowIfInFrame();
        _nodes.Add(node);
        _records.Add(new RenderGraphNodeRecord(node));
    }

    /// <summary>
    /// Registers a node immediately before <paramref name="anchor"/>. Use this to keep
    /// a fixed sentinel node (e.g. the pipeline's final blit) at the end of the graph
    /// while new nodes are appended in front of it. The graph takes ownership and
    /// disposes the node (when <see cref="System.IDisposable"/>) with itself.
    /// </summary>
    /// <param name="anchor">The registered node before which <paramref name="node"/> is inserted.</param>
    /// <param name="node">The node to register.</param>
    /// <exception cref="InvalidOperationException">The anchor node is not registered in this graph.</exception>
    public void InsertBefore(IRenderGraphNode anchor, IRenderGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(node);
        ThrowIfInFrame();
        int index = _nodes.IndexOf(anchor);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"The anchor node '{anchor.GetType().Name}' is not registered in this render graph.");
        }
        _nodes.Insert(index, node);
        _records.Insert(index, new RenderGraphNodeRecord(node));
    }

    /// <summary>
    /// Registers a node immediately after <paramref name="anchor"/>. The graph takes
    /// ownership and disposes the node (when <see cref="System.IDisposable"/>) with
    /// itself.
    /// </summary>
    /// <param name="anchor">The registered node after which <paramref name="node"/> is inserted.</param>
    /// <param name="node">The node to register.</param>
    /// <exception cref="InvalidOperationException">The anchor node is not registered in this graph.</exception>
    public void InsertAfter(IRenderGraphNode anchor, IRenderGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ArgumentNullException.ThrowIfNull(node);
        ThrowIfInFrame();
        int index = _nodes.IndexOf(anchor);
        if (index < 0)
        {
            throw new InvalidOperationException(
                $"The anchor node '{anchor.GetType().Name}' is not registered in this render graph.");
        }
        _nodes.Insert(index + 1, node);
        _records.Insert(index + 1, new RenderGraphNodeRecord(node));
    }

    /// <summary>
    /// Removes a node previously added via <see cref="Use"/>. The node is not disposed.
    /// </summary>
    public bool Remove(IRenderGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ThrowIfInFrame();
        int index = _nodes.IndexOf(node);
        if (index < 0)
        {
            return false;
        }
        _nodes.RemoveAt(index);
        _records.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Gets the first node of the given type, or null when the graph has none.
    /// </summary>
    public T? Get<T>() where T : class, IRenderGraphNode
    {
        for (int i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i] is T node)
            {
                return node;
            }
        }
        return null;
    }

    /// <summary>
    /// Creates a transient texture resource: pooled, lifetime-scheduled, and eligible
    /// for aliasing with other transients whose lifetimes do not overlap. The resource
    /// materializes immediately, so its <see cref="RenderGraphTexture.Texture"/> facade
    /// can be bound to materials right away.
    /// </summary>
    /// <param name="descriptor">The resource description.</param>
    /// <exception cref="ArgumentException">The depth source is invalid (not a transient
    /// of this graph, missing depth attachments, or mismatched depth formats).</exception>
    public RenderGraphTexture CreateTransient(in RenderGraphTextureDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor.Layout);
        ThrowIfInFrame();

        RenderGraphTexture? depthSource = descriptor.DepthSource;
        if (depthSource != null)
        {
            if (depthSource.Kind != RenderGraphTexture.ResourceKind.Transient || depthSource.Id < 0)
            {
                throw new ArgumentException(
                    $"The depth source of '{descriptor.Name}' must be a transient resource created by this graph.",
                    nameof(descriptor));
            }
            if (!descriptor.Layout.Depth.HasValue || depthSource.Layout!.Depth == null)
            {
                throw new ArgumentException(
                    $"'{descriptor.Name}' shares the depth of '{depthSource.Name}': both layouts must declare a depth attachment.",
                    nameof(descriptor));
            }
            if (descriptor.Layout.Depth.Value.Format != depthSource.Layout.Depth.Value.Format)
            {
                throw new ArgumentException(
                    $"'{descriptor.Name}' shares the depth of '{depthSource.Name}' but the depth formats differ " +
                    $"({descriptor.Layout.Depth.Value.Format} vs {depthSource.Layout.Depth.Value.Format}).",
                    nameof(descriptor));
            }
        }

        RenderGraphTexture resource = RenderGraphTexture.CreateTransient(
            descriptor,
            ResolveSize(descriptor.Width, _width, descriptor.ResolutionScale),
            ResolveSize(descriptor.Height, _height, descriptor.ResolutionScale));
        resource.Id = _resources.Count;
        resource.ComputeSlotKeys();
        _resources.Add(resource);

        // Materialize immediately so the facade can be bound to materials before the
        // first frame. The assignment takes part in the next frame's allocation walk
        // like any other (as the resource's sticky entry).
        AssignTransient(resource);
        return resource;
    }

    /// <summary>
    /// Imports a caller-owned persistent render texture (e.g. cross-frame temporal
    /// history) as a graph resource. The graph references but never pools, rebinds or
    /// disposes it.
    /// </summary>
    public RenderGraphTexture Import(RenderTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ThrowIfInFrame();
        RenderGraphTexture resource = RenderGraphTexture.CreateImported(texture);
        resource.Id = _resources.Count;
        _resources.Add(resource);
        return resource;
    }

    /// <summary>
    /// Destroys a transient resource ahead of the graph's own lifetime, tombstoning
    /// it in place: the resource stays in the resource table (ids remain stable) but
    /// its facade is disposed and it is skipped by the allocation walk, resize and
    /// disposal. Use this when the owning node is removed dynamically (e.g. a
    /// post-process node's private output). The graph still owns the pool entries
    /// the resource last held — they become idle and are reused by other transients.
    /// </summary>
    /// <param name="resource">The transient resource to destroy.</param>
    public void DestroyTransient(RenderGraphTexture resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ThrowIfInFrame();
        if (resource.Kind != RenderGraphTexture.ResourceKind.Transient || resource.IsDestroyed)
        {
            return;
        }
        resource.IsDestroyed = true;
        resource.Facade?.Dispose();
    }

    /// <summary>
    /// Runs one frame: Setup (dependency declaration) → Compile (validation, culling,
    /// lifetimes) → Assign (pooled texture allocation walk) → Execute (node work).
    /// </summary>
    /// <param name="destination">The final output frame buffer (e.g. the swapchain),
    /// or null for a minimized/headless view.</param>
    /// <returns>The number of alive nodes that executed this frame.</returns>
    public int Execute(GPUFrameBuffer? destination)
    {
        ThrowIfInFrame();
        _inFrame = true;
        HasDestinationThisFrame = destination != null;
        Profiler?.BeginFrame();
        try
        {
            // Setup: capture this frame's declarations in registration order.
            for (int i = 0; i < _records.Count; i++)
            {
                RenderGraphNodeRecord record = _records[i];
                record.ResetPerFrame();
                if (!record.EnabledThisFrame)
                {
                    continue;
                }
                _builder.Attach(record);
                try
                {
                    record.Node.Setup(_builder);
                }
                finally
                {
                    _builder.Detach();
                }
            }

            _compiler.Compile(_records, _resources);

            // Assign: restart the walk and hand out pooled textures for this frame's
            // lifetimes before any node executes.
            _pool.BeginFrame();
            AssignResources(_compiler.FirstTouch, _compiler.LastTouch);

            _context.Reset(destination, _rendering.GlobalRenderDataBuffer.Value.DeltaTime);
            _context.Profiler = Profiler;

            ReadOnlySpan<bool> alive = _compiler.Alive;
            int executed = 0;
            _sharedContext.Open();
            try
            {
                for (int i = 0; i < _records.Count; i++)
                {
                    if (alive[i])
                    {
                        _records[i].Node.Execute(_context);
                        executed++;
                    }
                }
            }
            finally
            {
                // One submission per frame for the whole graph. If a node threw with
                // its pass still open, abort the buffer instead so the next frame
                // starts from a clean state.
                if (_sharedContext.IsBufferOpen)
                {
                    if (_sharedContext.IsPassOpen)
                    {
                        _sharedContext.Abort();
                    }
                    else
                    {
                        _sharedContext.Submit();
                    }
                }
            }
            return executed;
        }
        finally
        {
            Profiler?.EndFrame();
            _inFrame = false;
        }
    }

    /// <summary>
    /// Resizes the graph viewport. Graph-relative transients are rematerialized at the
    /// new size and pool entries of the old size are pruned (disposed through the
    /// deferred destruction path, keeping in-flight GPU work valid); all nodes are
    /// notified via <see cref="IRenderNode.Resize"/>.
    /// </summary>
    public void Resize(uint width, uint height)
    {
        ThrowIfInFrame();
        if (width == _width && height == _height)
        {
            return;
        }
        _width = width;
        _height = height;

        // Restart the pool's walk state so the reassignment below can recover
        // sticky entries that were still occupied at the end of last frame's walk.
        _pool.BeginFrame();

        // Re-resolve sizes and reassign: resources whose key is unchanged by the
        // resize (absolute-size transients, or scales resolving to the same size)
        // recover their sticky assignment and keep their backing without a rebind;
        // resized resources miss their sticky entry (it sits under the old-size key)
        // and materialize/rebind at the new size.
        for (int i = 0; i < _resources.Count; i++)
        {
            RenderGraphTexture resource = _resources[i];
            if (resource.Kind != RenderGraphTexture.ResourceKind.Transient || resource.IsDestroyed)
            {
                continue;
            }
            resource.ResolvedWidth = ResolveSize(resource.AbsoluteWidth, width, resource.ResolutionScale);
            resource.ResolvedHeight = ResolveSize(resource.AbsoluteHeight, height, resource.ResolutionScale);
            resource.ComputeSlotKeys();
            AssignTransient(resource);
        }

        // Prune pool entries no resource kept: pool keys are size-qualified, so
        // entries of the old size could never be matched again and would otherwise
        // accumulate on every resize (e.g. dragging a window edge).
        _keepSet.Clear();
        for (int i = 0; i < _resources.Count; i++)
        {
            RenderGraphTexture resource = _resources[i];
            if (resource.Kind != RenderGraphTexture.ResourceKind.Transient || resource.IsDestroyed)
            {
                continue;
            }
            if (resource.ColorAttachments != null)
            {
                foreach (PooledAttachment color in resource.ColorAttachments)
                {
                    _keepSet.Add(color);
                }
            }
            if (resource.DepthAttachment != null)
            {
                _keepSet.Add(resource.DepthAttachment);
            }
        }
        _pool.PruneExcept(_keepSet);

        for (int i = 0; i < _nodes.Count; i++)
        {
            _nodes[i].Resize(width, height);
        }
    }

    /// <summary>
    /// Performs the allocation walk: used transients are visited in first-touch order;
    /// assignments whose lifetime ended strictly before the current resource starts
    /// are released to the pool (making them the preferred alias candidates), then
    /// every slot of the current resource is assigned. Walking in first-touch order
    /// releases assignments as late as possible, which is what allows non-overlapping
    /// lifetimes to share one texture while overlapping lifetimes keep stable,
    /// dedicated assignments across frames.
    /// </summary>
    private void AssignResources(ReadOnlySpan<int> firstTouch, ReadOnlySpan<int> lastTouch)
    {
        // Collect the used transient resources and insertion-sort them by
        // (first touch, depth chain length, index). The depth chain orders a shared
        // depth source before its dependents when both start at the same node.
        if (_sortedIndices.Length < _resources.Count)
        {
            _sortedIndices = new int[Math.Max(_resources.Count, _sortedIndices.Length * 2)];
        }
        int used = 0;
        for (int i = 0; i < _resources.Count; i++)
        {
            if (_resources[i].Kind == RenderGraphTexture.ResourceKind.Transient
                && !_resources[i].IsDestroyed
                && firstTouch[i] >= 0)
            {
                _sortedIndices[used++] = i;
            }
        }
        for (int i = 1; i < used; i++)
        {
            int index = _sortedIndices[i];
            int first = firstTouch[index];
            int chain = DepthChain(_resources[index]);
            int j = i - 1;
            while (j >= 0)
            {
                int other = _sortedIndices[j];
                int otherFirst = firstTouch[other];
                if (otherFirst < first || (otherFirst == first && DepthChain(_resources[other]) <= chain))
                {
                    break;
                }
                _sortedIndices[j + 1] = other;
                j--;
            }
            _sortedIndices[j + 1] = index;
        }

        _assigned.Clear();
        for (int s = 0; s < used; s++)
        {
            int index = _sortedIndices[s];
            RenderGraphTexture resource = _resources[index];

            // Release assignments whose lifetime ended strictly before this one
            // starts. Equal boundaries (one resource's last touch is the other's
            // first touch) mean both are live while that node executes and must not
            // alias.
            for (int a = _assigned.Count - 1; a >= 0; a--)
            {
                if (_assigned[a].LastTouch < firstTouch[index])
                {
                    ReleaseAssignment(_assigned[a].Resource);
                    _assigned.RemoveAt(a);
                }
            }

            AssignTransient(resource);
            _assigned.Add((resource, lastTouch[index]));
        }
    }

    /// <summary>
    /// Assigns pooled attachments to a transient, rebinding its facade only when the
    /// assignment changed. In steady state the pool returns the identical attachments
    /// and this reduces to reference comparisons.
    /// </summary>
    private void AssignTransient(RenderGraphTexture resource)
    {
        TexturePoolKey[] keys = resource.ColorKeys!;
        PooledAttachment[] colors = resource.ColorAttachments ?? new PooledAttachment[keys.Length];
        bool changed = resource.ComposedFrameBuffer == null;
        for (int i = 0; i < keys.Length; i++)
        {
            PooledAttachment assigned = (PooledAttachment)_pool.Allocate(keys[i], colors[i], resource.Name);
            if (!ReferenceEquals(colors[i], assigned))
            {
                changed = true;
            }
            colors[i] = assigned;
        }

        PooledAttachment? depth = resource.DepthAttachment;
        if (resource.DepthSource != null)
        {
            PooledAttachment? shared = resource.DepthSource.DepthAttachment;
            if (shared == null)
            {
                // Unreachable when compilation passed: the depth source sorts before
                // its dependents and is therefore always assigned first.
                throw new InvalidOperationException(
                    $"Render graph internal error: the depth source '{resource.DepthSource.Name}' of '{resource.Name}' is not assigned.");
            }
            if (!ReferenceEquals(depth, shared))
            {
                changed = true;
            }
            depth = shared;
        }
        else if (resource.OwnDepthKey.HasValue)
        {
            TexturePoolKey key = resource.OwnDepthKey.Value;
            PooledAttachment assigned = (PooledAttachment)_pool.Allocate(key, depth, resource.Name);
            if (!ReferenceEquals(depth, assigned))
            {
                changed = true;
            }
            depth = assigned;
        }
        else
        {
            depth = null;
        }

        resource.ColorAttachments = colors;
        resource.DepthAttachment = depth;

        if (!changed)
        {
            return;
        }

        GPUFrameBuffer frameBuffer = ComposeFrameBuffer(resource, colors, depth);
        if (resource.Facade == null)
        {
            GPUSampler sampler = _device.GetSampler(resource.Filter, AddressMode.ClampToEdge);
            resource.SetFacade(new RenderTexture(_rendering, frameBuffer, sampler));
        }
        else
        {
            // Rebind disposes the previously composed frame buffer internally.
            resource.Facade.Rebind(frameBuffer);
        }
        resource.ComposedFrameBuffer = frameBuffer;
    }

    /// <summary>Releases a transient's current assignment to the pool as reuse
    /// candidates. A shared depth attachment is owned (and released) by its source.</summary>
    private void ReleaseAssignment(RenderGraphTexture resource)
    {
        PooledAttachment[]? colors = resource.ColorAttachments;
        TexturePoolKey[]? keys = resource.ColorKeys;
        if (colors != null && keys != null)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                _pool.ReleaseExpired(keys[i], colors[i]);
            }
        }
        if (resource.DepthSource == null && resource.DepthAttachment != null && resource.OwnDepthKey.HasValue)
        {
            _pool.ReleaseExpired(resource.OwnDepthKey.Value, resource.DepthAttachment);
        }
        // The last assignment stays on the resource for steady-state reference
        // comparison (and as its sticky entry next frame); its contents are only
        // valid inside the resource's lifetime.
    }

    /// <summary>Composes a non-owning frame buffer from pooled attachments.</summary>
    private GPUFrameBuffer ComposeFrameBuffer(RenderGraphTexture resource, PooledAttachment[] colors, PooledAttachment? depth)
    {
        GPUTexture[] colorTextures = colors.Length == 0 ? Array.Empty<GPUTexture>() : new GPUTexture[colors.Length];
        GPUTextureView[] colorViews = colors.Length == 0 ? Array.Empty<GPUTextureView>() : new GPUTextureView[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colorTextures[i] = colors[i].Texture;
            colorViews[i] = colors[i].AttachmentView;
        }

        return _device.CreateExternalFrameBuffer(new ExternalFrameBufferDescriptor(
            resource.Layout!,
            colorTextures,
            colorViews,
            resource.ResolvedWidth,
            resource.ResolvedHeight,
            resource.Name + "_framebuffer")
        {
            DepthStencil = depth?.Texture,
            DepthStencilView = depth?.AttachmentView,
            DepthView = depth?.DepthView,
            StencilView = depth?.StencilView,
        });
    }

    /// <summary>The pool factory: materializes a pooled attachment texture on the device.</summary>
    private PooledAttachment CreatePooledAttachment(TexturePoolKey key, string name)
    {
        return PooledAttachment.Create(_device, key, name + "_pooled");
    }

    /// <summary>The number of shared-depth hops below the resource (0 = own or no depth).</summary>
    private static int DepthChain(RenderGraphTexture resource)
    {
        int depth = 0;
        for (RenderGraphTexture? source = resource.DepthSource; source != null; source = source.DepthSource)
        {
            depth++;
        }
        return depth;
    }

    private static uint ResolveSize(uint absolute, uint graphSize, float scale)
    {
        if (absolute > 0)
        {
            return absolute;
        }
        uint resolved = (uint)(graphSize * scale);
        return Math.Max(resolved, 1u);
    }

    private void ThrowIfInFrame()
    {
        if (_inFrame)
        {
            throw new InvalidOperationException(
                "RenderGraph.Use/InsertBefore/Remove/CreateTransient/Import/Resize/Execute must not be called from inside a node's Setup or Execute.");
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            for (int i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i] is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _nodes.Clear();
            _records.Clear();

            // The facades own the composed frame buffers; the pool owns the textures.
            for (int i = 0; i < _resources.Count; i++)
            {
                RenderGraphTexture resource = _resources[i];
                if (resource.Kind == RenderGraphTexture.ResourceKind.Transient && !resource.IsDestroyed)
                {
                    resource.Facade?.Dispose();
                }
            }
            _resources.Clear();
            _pool.Dispose();
            _sharedContext.Dispose();
        }
    }
}
