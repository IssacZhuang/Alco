// Per-frame setup builder, pass access builder, and execution context for RenderGraph.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>
/// Per-frame setup surface passed to <see cref="IRenderGraphNode.Setup"/> for creating frame-scoped resources
/// and declaring passes. A single reused instance valid only during Setup.
/// </summary>
public sealed class RenderGraphBuilder
{
    private RenderGraph? _graph;
    private IRenderGraphNode? _currentNode;
    private int _currentNodeIndex = -1;

    internal RenderGraphBuilder()
    {
    }

    internal void Attach(RenderGraph graph, IRenderGraphNode node, int nodeIndex)
    {
        _graph = graph;
        _currentNode = node;
        _currentNodeIndex = nodeIndex;
    }

    internal void Detach()
    {
        _graph = null;
        _currentNode = null;
        _currentNodeIndex = -1;
    }

    /// <summary>The current graph viewport extent in pixels.</summary>
    public Extent2D Viewport => Graph.Viewport;

    /// <summary>Whether the current frame has a valid drawable output target.</summary>
    public bool HasOutput => Graph.HasOutputThisFrame;

    /// <summary>
    /// The handle for the frame's output target (swapchain or offscreen texture), or an invalid handle when
    /// <see cref="HasOutput"/> is false.
    /// </summary>
    public RGTexture Output => Graph.CurrentOutputHandle;

    /// <summary>Creates a frame-scoped transient texture pooled and aliased by the graph for this frame.</summary>
    /// <param name="desc">The transient texture description.</param>
    /// <returns>A texture handle valid for the remainder of this frame.</returns>
    public RGTexture CreateTexture(in RGTextureDesc desc)
    {
        return Graph.AddFrameTransientTexture(in desc);
    }

    /// <summary>Creates a frame-scoped transient buffer pooled and aliased by the graph for this frame.</summary>
    /// <param name="desc">The transient buffer description.</param>
    /// <returns>A buffer handle valid for the remainder of this frame.</returns>
    public RGBuffer CreateBuffer(in RGBufferDesc desc)
    {
        return Graph.AddFrameTransientBuffer(in desc);
    }

    /// <summary>Imports a caller-owned texture for this frame without transferring ownership.</summary>
    /// <param name="texture">The live texture to track.</param>
    /// <param name="name">Optional diagnostic name.</param>
    /// <param name="finalUsage">Optional external access performed after graph execution to emit a trailing barrier for.</param>
    /// <returns>A texture handle valid for the remainder of this frame.</returns>
    public RGTexture ImportTexture(Texture texture, string name = "", RGUsage finalUsage = RGUsage.None)
    {
        return Graph.AddFrameImportedTexture(texture, name, finalUsage);
    }

    /// <summary>Imports a caller-owned buffer for this frame without transferring ownership.</summary>
    /// <param name="buffer">The live buffer to track.</param>
    /// <param name="name">Optional diagnostic name.</param>
    /// <param name="finalUsage">Optional external access performed after graph execution to emit a trailing barrier for.</param>
    /// <returns>A buffer handle valid for the remainder of this frame.</returns>
    public RGBuffer ImportBuffer(Buffer buffer, string name = "", RGUsage finalUsage = RGUsage.None)
    {
        return Graph.AddFrameImportedBuffer(buffer, name, finalUsage);
    }

    /// <summary>Gets the resolved texel extent of a valid texture handle for this frame.</summary>
    /// <param name="texture">A valid texture handle belonging to this graph.</param>
    /// <returns>The resolved extent in texels.</returns>
    public Extent3D GetExtent(RGTexture texture) => Graph.GetTextureExtent(texture);

    /// <summary>Gets the pixel format of a valid texture handle.</summary>
    /// <param name="texture">A valid texture handle belonging to this graph.</param>
    /// <returns>The texture format.</returns>
    public Format GetFormat(RGTexture texture) => Graph.GetTextureFormat(texture);

    /// <summary>Reports whether <paramref name="texture"/> supports all accesses in <paramref name="usage"/> on this device.</summary>
    /// <param name="texture">A valid texture handle belonging to this graph.</param>
    /// <param name="usage">The intended pass usage flags.</param>
    /// <returns><see langword="true"/> when the texture and device format support <paramref name="usage"/>.</returns>
    public bool SupportsUsage(RGTexture texture, RGUsage usage) => Graph.SupportsTextureUsage(texture, usage);

    /// <summary>Gets the logical byte size of a valid buffer handle.</summary>
    /// <param name="buffer">A valid buffer handle belonging to this graph.</param>
    /// <returns>The buffer size in bytes.</returns>
    public ulong GetSizeBytes(RGBuffer buffer) => Graph.GetBufferSizeBytes(buffer);

    /// <summary>Adds a pass owned by the currently setting-up node.</summary>
    /// <param name="name">The pass name shown in profiling and GPU debug groups.</param>
    /// <param name="passId">A node-local sub-pass identifier passed back to <see cref="IRenderGraphNode.Execute"/>.</param>
    /// <returns>A zero-allocation pass builder for declaring resource reads, writes, and output status.</returns>
    public RenderGraphPassBuilder AddPass(string name, int passId = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        IRenderGraphNode node = _currentNode ?? throw new InvalidOperationException("RenderGraphBuilder is only valid during IRenderGraphNode.Setup.");
        int passIndex = Graph.AddPassRecord(name, node, _currentNodeIndex, passId);
        return new RenderGraphPassBuilder(Graph, passIndex);
    }

    /// <summary>Adds a pass owned by the currently setting-up node, dispatching execution to <paramref name="node"/>.</summary>
    /// <param name="name">The pass name shown in profiling and GPU debug groups.</param>
    /// <param name="node">The node whose <see cref="IRenderGraphNode.Execute"/> will be invoked if this pass survives culling.</param>
    /// <param name="passId">A node-local sub-pass identifier passed back to <see cref="IRenderGraphNode.Execute"/>.</param>
    /// <returns>A zero-allocation pass builder for declaring resource reads, writes, and output status.</returns>
    public RenderGraphPassBuilder AddPass(string name, IRenderGraphNode node, int passId = 0)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(node);
        int passIndex = Graph.AddPassRecord(name, node, _currentNodeIndex, passId);
        return new RenderGraphPassBuilder(Graph, passIndex);
    }

    internal RGTexture ImportDryRunTexture(object token, Extent3D extent, Format format, TextureUsage usage, string name = "",
        RGUsage finalUsage = RGUsage.None)
    {
        return Graph.AddFrameDryRunImportedTexture(token, extent, format, usage, name, finalUsage);
    }

    private RenderGraph Graph => _graph ?? throw new InvalidOperationException("RenderGraphBuilder is only valid during IRenderGraphNode.Setup.");
}

/// <summary>Declares resource reads, writes, and output status for a single render graph pass.</summary>
public readonly struct RenderGraphPassBuilder
{
    private readonly RenderGraph _graph;
    private readonly int _passIndex;

    internal RenderGraphPassBuilder(RenderGraph graph, int passIndex)
    {
        _graph = graph;
        _passIndex = passIndex;
    }

    /// <summary>Declares a read-only dependency on a texture.</summary>
    /// <param name="texture">The texture handle read by this pass.</param>
    /// <param name="usage">Nonempty read-only usage flags (must not include write flags).</param>
    public void Read(RGTexture texture, RGUsage usage)
    {
        _graph.AddPassTextureAccess(_passIndex, texture, usage, isLogicalRead: true, isLogicalWrite: false);
    }

    /// <summary>
    /// Declares that this pass produces (overwrites) a texture without depending on its prior contents.
    /// The usage flags describe the hardware accesses performed during the pass (for example, <see cref="RGUsage.DepthStencil"/>
    /// for a cleared depth buffer that is both depth-tested and written).
    /// </summary>
    /// <param name="texture">The texture handle written by this pass.</param>
    /// <param name="usage">Usage flags including at least one write bit.</param>
    public void Write(RGTexture texture, RGUsage usage)
    {
        _graph.AddPassTextureAccess(_passIndex, texture, usage, isLogicalRead: false, isLogicalWrite: true);
    }

    /// <summary>Declares an in-place modification of a texture (depends on earlier content and produces updated content).</summary>
    /// <param name="texture">The texture handle modified by this pass.</param>
    /// <param name="usage">Usage flags including at least one write bit (typically combined read and write flags).</param>
    public void ReadWrite(RGTexture texture, RGUsage usage)
    {
        _graph.AddPassTextureAccess(_passIndex, texture, usage, isLogicalRead: true, isLogicalWrite: true);
    }

    /// <summary>Declares a read-only dependency on a buffer.</summary>
    /// <param name="buffer">The buffer handle read by this pass.</param>
    /// <param name="usage">Nonempty read-only buffer usage flags.</param>
    public void Read(RGBuffer buffer, RGUsage usage)
    {
        _graph.AddPassBufferAccess(_passIndex, buffer, usage, isLogicalRead: true, isLogicalWrite: false);
    }

    /// <summary>Declares that this pass produces (overwrites) a buffer without depending on its prior contents.</summary>
    /// <param name="buffer">The buffer handle written by this pass.</param>
    /// <param name="usage">Usage flags including at least one write bit.</param>
    public void Write(RGBuffer buffer, RGUsage usage)
    {
        _graph.AddPassBufferAccess(_passIndex, buffer, usage, isLogicalRead: false, isLogicalWrite: true);
    }

    /// <summary>Declares an in-place modification of a buffer (depends on earlier content and produces updated content).</summary>
    /// <param name="buffer">The buffer handle modified by this pass.</param>
    /// <param name="usage">Usage flags including at least one write bit.</param>
    public void ReadWrite(RGBuffer buffer, RGUsage usage)
    {
        _graph.AddPassBufferAccess(_passIndex, buffer, usage, isLogicalRead: true, isLogicalWrite: true);
    }

    /// <summary>Marks this pass as having externally visible side effects so dead-pass culling never removes it.</summary>
    public void ProducesOutput()
    {
        _graph.MarkPassProducesOutput(_passIndex);
    }
}

/// <summary>
/// Execution context passed to <see cref="IRenderGraphNode.Execute"/> for resolving graph handles to concrete
/// <see cref="Texture"/> and <see cref="Buffer"/> objects and recording commands onto the active <see cref="Rendering.RenderContext"/>.
/// </summary>
public readonly struct RenderGraphContext
{
    private readonly RenderGraph _graph;

    internal RenderGraphContext(RenderGraph graph, RenderContext renderContext, RenderGraphTarget output, float deltaTime)
    {
        _graph = graph;
        RenderContext = renderContext;
        Output = output;
        DeltaTime = deltaTime;
    }

    /// <summary>The active frame recording context; passes open render or compute scopes directly on it.</summary>
    public RenderContext RenderContext { get; }

    /// <summary>The rendering system owning the device and shared descriptor heaps.</summary>
    public RenderingSystem System => _graph.System;

    /// <summary>The frame's output target (swapchain or offscreen texture), or invalid when headless.</summary>
    public RenderGraphTarget Output { get; }

    /// <summary>Whether the frame has a valid drawable output target.</summary>
    public bool HasOutput => Output.IsValid;

    /// <summary>The frame delta time in seconds supplied to <see cref="RenderGraph.Execute"/>.</summary>
    public float DeltaTime { get; }

    /// <summary>The current graph viewport extent in pixels.</summary>
    public Extent2D Viewport => _graph.Viewport;

    /// <summary>Resolves a texture handle to its concrete <see cref="Texture"/> for this frame.</summary>
    /// <param name="handle">A live texture handle backed by a <see cref="Texture"/> (not a swapchain output).</param>
    /// <returns>The backing texture for this frame.</returns>
    public Texture GetTexture(RGTexture handle) => _graph.ResolveTexture(handle);

    /// <summary>Tries to resolve a texture handle to its concrete <see cref="Texture"/>, returning null if invalid, culled, or swapchain-backed.</summary>
    /// <param name="handle">The texture handle to resolve.</param>
    /// <returns>The backing texture, or null.</returns>
    public Texture? TryGetTexture(RGTexture handle) => _graph.TryResolveTexture(handle);

    /// <summary>Resolves a texture handle (including <see cref="RenderGraphBuilder.Output"/>) to its mip-zero <see cref="RenderTarget"/>.</summary>
    /// <param name="handle">A valid texture handle.</param>
    /// <returns>The attachment render target.</returns>
    public RenderTarget GetRenderTarget(RGTexture handle) => _graph.ResolveRenderTarget(handle);

    /// <summary>Resolves a texture handle to a specific mip level and array slice <see cref="RenderTarget"/>.</summary>
    /// <param name="handle">A valid texture handle backed by a <see cref="Texture"/>.</param>
    /// <param name="mipLevel">The zero-based mip level.</param>
    /// <param name="arraySlice">The zero-based array slice.</param>
    /// <returns>The attachment render target.</returns>
    public RenderTarget GetRenderTarget(RGTexture handle, uint mipLevel, uint arraySlice = 0) =>
        _graph.ResolveTexture(handle).GetRenderTarget(mipLevel, arraySlice);

    /// <summary>Begins a single-color-target render pass over <paramref name="colorTarget"/>.</summary>
    /// <param name="colorTarget">The color attachment handle.</param>
    /// <param name="load">Color attachment load operation.</param>
    /// <param name="store">Color attachment store operation.</param>
    /// <param name="clearColor">Clear color when <paramref name="load"/> is <see cref="LoadOp.Clear"/>.</param>
    /// <returns>The active render pass scope.</returns>
    public RenderContext.RenderScope BeginRenderPass(RGTexture colorTarget, LoadOp load = LoadOp.Load,
        StoreOp store = StoreOp.Store, Vector4 clearColor = default)
    {
        ColorAttachment color = new()
        {
            Target = GetRenderTarget(colorTarget),
            Load = load,
            Store = store,
            Clear = clearColor,
        };
        return RenderContext.BeginRenderPass(new RenderingDesc
        {
            Colors = MemoryMarshal.CreateReadOnlySpan(ref color, 1),
        });
    }

    /// <summary>Begins a color+depth (or depth-only when <paramref name="colorTarget"/> is invalid) render pass.</summary>
    /// <param name="colorTarget">The color attachment handle, or default for depth-only rendering.</param>
    /// <param name="depthTarget">The depth attachment handle, or default when absent.</param>
    /// <param name="colorLoad">Color attachment load operation.</param>
    /// <param name="colorStore">Color attachment store operation.</param>
    /// <param name="clearColor">Clear color when <paramref name="colorLoad"/> is <see cref="LoadOp.Clear"/>.</param>
    /// <param name="depthLoad">Depth attachment load operation.</param>
    /// <param name="depthStore">Depth attachment store operation.</param>
    /// <param name="clearDepth">Clear depth value when <paramref name="depthLoad"/> is <see cref="LoadOp.Clear"/>.</param>
    /// <returns>The active render pass scope.</returns>
    public RenderContext.RenderScope BeginRenderPass(RGTexture colorTarget, RGTexture depthTarget,
        LoadOp colorLoad = LoadOp.Load, StoreOp colorStore = StoreOp.Store, Vector4 clearColor = default,
        LoadOp depthLoad = LoadOp.Load, StoreOp depthStore = StoreOp.Store, float clearDepth = 1.0f)
    {
        ColorAttachment color = colorTarget.IsValid
            ? new ColorAttachment
            {
                Target = GetRenderTarget(colorTarget),
                Load = colorLoad,
                Store = colorStore,
                Clear = clearColor,
            }
            : default;
        DepthAttachment depth = depthTarget.IsValid
            ? new DepthAttachment
            {
                Target = GetRenderTarget(depthTarget),
                Load = depthLoad,
                Store = depthStore,
                Clear = clearDepth,
            }
            : new DepthAttachment();
        return RenderContext.BeginRenderPass(new RenderingDesc
        {
            Colors = colorTarget.IsValid ? MemoryMarshal.CreateReadOnlySpan(ref color, 1) : ReadOnlySpan<ColorAttachment>.Empty,
            Depth = depth,
        });
    }

    internal GPURenderView GetRenderViewInternal(RGTexture handle) => _graph.ResolveRenderView(handle);

    internal GPUTexture GetNativeTextureInternal(RGTexture handle) => _graph.ResolveNativeTexture(handle);

    /// <summary>Gets the resolved texel extent of a texture handle.</summary>
    /// <param name="handle">A valid texture handle.</param>
    /// <returns>The extent in texels.</returns>
    public Extent3D GetExtent(RGTexture handle) => _graph.GetTextureExtent(handle);

    /// <summary>Gets the format of a texture handle.</summary>
    /// <param name="handle">A valid texture handle.</param>
    /// <returns>The pixel format.</returns>
    public Format GetFormat(RGTexture handle) => _graph.GetTextureFormat(handle);

    /// <summary>Resolves a buffer handle to its concrete <see cref="Buffer"/> for this frame.</summary>
    /// <param name="handle">A valid buffer handle.</param>
    /// <returns>The backing buffer for this frame.</returns>
    public Buffer GetBuffer(RGBuffer handle) => _graph.ResolveBuffer(handle);

    /// <summary>Tries to resolve a buffer handle to its concrete <see cref="Buffer"/>, returning null if invalid or culled.</summary>
    /// <param name="handle">The buffer handle to resolve.</param>
    /// <returns>The backing buffer, or null.</returns>
    public Buffer? TryGetBuffer(RGBuffer handle) => _graph.TryResolveBuffer(handle);
}
