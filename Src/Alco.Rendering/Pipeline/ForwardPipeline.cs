
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The forward pipeline: a minimal <see cref="RenderGraph"/> composition — a clear
/// node, user content nodes drawing into the scene content target
/// (<see cref="RGNode_SceneContent"/>), chain transform nodes
/// (<see cref="RGNode_ChainTransform"/>: color grading, bloom, tone mapping, ...), and a
/// final blit into the destination. The owner composes the frame purely by ordering
/// nodes via <see cref="Use"/> — there is no separate post-processing concept.
/// <br/>The pipeline is a plain object, created and driven manually by its owner (the
/// engine for the main view, game code for additional views):
/// <list type="number">
/// <item><see cref="Use"/> the nodes in the order they should execute.</item>
/// <item><see cref="Render"/>: clears the scene texture, then runs the graph into the
/// final destination (typically the swapchain frame buffer).</item>
/// <item><see cref="Resize"/> when the view size changes.</item>
/// </list>
/// Everything the pipeline does is public API: the same frame can be composed by hand
/// from <see cref="RenderGraph"/>, <see cref="RGNode_Clear"/>, <see cref="RenderChain"/>
/// and <see cref="RGNode_Blit"/>, and any stage of this pipeline can be replaced or
/// reordered through <see cref="Graph"/>.
/// </summary>
public sealed class ForwardPipeline : AutoDisposable
{
    private readonly RenderingSystem _rendering;
    private readonly GPUAttachmentLayout _sceneLayout;
    private readonly GPUAttachmentLayout _postProcessLayout;
    private readonly RenderGraph _graph;
    private readonly RenderChain _chain = new();
    private readonly RenderGraphTexture _sceneResource;
    private readonly RGNode_Clear _clearNode;
    private readonly RGNode_Blit _blitNode;

    /// <summary>
    /// The scene render texture the chain's content nodes draw into (via the chain,
    /// never directly). The facade's object identity stays stable across resizes.
    /// </summary>
    public RenderTexture SceneTexture => _sceneResource.Texture;

    /// <summary>
    /// The render graph driving the frame. Compose freely: insert nodes before any
    /// existing node (<see cref="RenderGraph.InsertBefore"/>) or replace the clear or
    /// the final blit outright (<see cref="RenderGraph.Remove"/> + insertion).
    /// </summary>
    public RenderGraph Graph => _graph;

    /// <summary>
    /// The content chain threading the scene texture through the graph's chain nodes.
    /// Reset to <see cref="SceneColorResource"/> at the start of every
    /// <see cref="Render"/> call.
    /// </summary>
    public RenderChain Chain => _chain;

    /// <summary>The pipeline's scene content resource (chain root).</summary>
    public RenderGraphTexture SceneColorResource => _sceneResource;

    /// <summary>The attachment layout of the scene render texture.</summary>
    public GPUAttachmentLayout SceneLayout => _sceneLayout;

    /// <summary>
    /// The color-only sibling of the scene layout, for the output transients of
    /// chain transform nodes (post-process effects).
    /// </summary>
    public GPUAttachmentLayout PostProcessLayout => _postProcessLayout;

    /// <summary>The final node of the pipeline: blits the chain tail into the destination.</summary>
    public RGNode_Blit FinalBlit => _blitNode;

    /// <summary>
    /// The color the scene texture is cleared to at the start of <see cref="Render"/>.
    /// Depth and stencil are always cleared to 1 and 0.
    /// </summary>
    public ColorFloat ClearColor
    {
        get => _clearNode.ClearColor;
        set => _clearNode.ClearColor = value;
    }

    /// <summary>
    /// Creates a forward pipeline with its scene render texture.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="sceneLayout">The attachment layout of the scene render texture
    /// (e.g. <see cref="RenderingSystem.PreferredSDRPass"/> or
    /// <see cref="RenderingSystem.PreferredHDRPass"/> for HDR content processors).
    /// Stays owned by the caller.</param>
    /// <param name="blitShader">The shader the final blit uses for the plain copy.</param>
    /// <param name="width">The initial scene texture width in pixels.</param>
    /// <param name="height">The initial scene texture height in pixels.</param>
    public ForwardPipeline(RenderingSystem rendering, GPUAttachmentLayout sceneLayout, Shader blitShader, uint width, uint height)
    {
        _rendering = rendering;
        _sceneLayout = sceneLayout;

        // Color-only sibling of the scene layout for chain transform outputs.
        _postProcessLayout = rendering.GraphicsDevice.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(sceneLayout.Colors[0].Format)],
            null,
            "forward_post_process"));

        _graph = new RenderGraph(rendering, width, height, "forward");
        _sceneResource = _graph.CreateTransient(new RenderGraphTextureDescriptor(
            sceneLayout, name: "forward_scene"));

        _clearNode = new RGNode_Clear(rendering, _sceneResource,
            [new ClearColorData(0, ColorFloat.Black)], clearDepth: 1.0f, name: "forward_clear");
        _blitNode = new RGNode_Blit(rendering, _graph, _chain, blitShader);

        _graph.Use(_clearNode);
        _graph.Use(_blitNode);
    }

    /// <summary>
    /// Registers a graph node into the pipeline, immediately before the final blit
    /// (nodes run in registration order). This is a convenience for
    /// <c>Graph.InsertBefore(FinalBlit, node)</c>; register at any other position
    /// through <see cref="Graph"/> directly. The graph takes ownership: nodes
    /// implementing <see cref="System.IDisposable"/> are disposed with the pipeline.
    /// </summary>
    public void Use(IRenderGraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _graph.InsertBefore(_blitNode, node);
    }

    /// <summary>
    /// Removes a node previously added via <see cref="Use"/> or <see cref="Graph"/>.
    /// The node is not disposed; transients it created remain allocated until destroyed
    /// (<see cref="RenderGraph.DestroyTransient"/>) or the node is disposed.
    /// </summary>
    public bool Remove(IRenderGraphNode node)
    {
        return _graph.Remove(node);
    }

    /// <summary>
    /// Gets the first node of the given type, or null when the graph has none.
    /// </summary>
    public T? Get<T>() where T : class, IRenderNode
    {
        IReadOnlyList<IRenderGraphNode> nodes = _graph.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (nodes[i] is T node)
            {
                return node;
            }
        }
        return null;
    }

    /// <summary>
    /// The registered nodes, in execution order.
    /// </summary>
    public IReadOnlyList<IRenderGraphNode> Nodes => _graph.Nodes;

    /// <summary>
    /// Renders the frame through the graph: the clear node clears the scene texture,
    /// content nodes draw into it, chain transforms process it, and the final blit
    /// lands the image in <paramref name="destination"/>. Disabled nodes and unconsumed
    /// work are culled by the graph automatically.
    /// </summary>
    /// <param name="destination">The final output frame buffer (e.g. the swapchain frame
    /// buffer). When null, content nodes still render into the scene texture and all
    /// chain transforms are skipped (minimized or headless view).</param>
    public void Render(GPUFrameBuffer? destination)
    {
        _chain.Reset(_sceneResource);
        _graph.Execute(destination);
    }

    /// <summary>
    /// Resizes the scene render texture. The graph rematerializes the graph-relative
    /// transients at the new size; their facades keep their object identity, so
    /// materials referencing them need no rebinding.
    /// </summary>
    public void Resize(uint width, uint height)
    {
        _graph.Resize(width, height);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // The graph disposes the registered nodes (clear, blit, user nodes) and
            // all transients, including the scene texture facade.
            _graph.Dispose();
            _postProcessLayout.Dispose();
        }
    }
}
