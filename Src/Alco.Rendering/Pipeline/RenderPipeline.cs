
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The universal render pipeline shell: owns a <see cref="RenderGraph"/>, a
/// <see cref="RenderChain"/> rooted at the scene color resource and a final blit
/// node, and drives the frame (<see cref="Render"/>) through the graph.
/// <br/>There is no forward/deferred pipeline type distinction: a pipeline is
/// nothing but a shell plus the nodes composed into its graph. A plain forward
/// pipeline is just this shell with content and chain transform nodes added via
/// <see cref="Use"/>; a deferred PBR pipeline is this shell with shadow, G-buffer,
/// deferred lighting and overlay nodes — assembled by the preset factory
/// (<see cref="RenderPipelines.CreatePBRDeferred"/>) or by hand from the same
/// public building blocks.
/// <br/>The pipeline is a plain object, created and driven manually by its owner
/// (the engine for the main view, game code for additional views):
/// <list type="number">
/// <item><see cref="Use"/> the nodes in the order they should execute.</item>
/// <item><see cref="Render"/>: clears the scene texture, then runs the graph into the
/// final destination (typically the swapchain frame buffer).</item>
/// <item><see cref="Resize"/> when the view size changes.</item>
/// </list>
/// Everything the pipeline does is public API: the same frame can be composed by hand
/// from <see cref="RenderGraph"/>, <see cref="RGNode_Clear"/>, <see cref="RenderChain"/>
/// and <see cref="RGNode_Blit"/>, and any stage of a pipeline can be replaced or
/// reordered through <see cref="Graph"/>.
/// </summary>
public sealed class RenderPipeline : AutoDisposable
{
    private readonly GPUAttachmentLayout _sceneLayout;
    private readonly GPUAttachmentLayout _postProcessLayout;
    private readonly RenderGraph _graph;
    private readonly RenderChain _chain;
    private readonly RenderGraphTexture _sceneResource;
    private readonly RGNode_Clear? _clearNode;
    private readonly RGNode_Blit _blitNode;

    /// <summary>
    /// Creates a minimal pipeline: a graph, a scene render texture, a clear node
    /// and the final blit. Content, overlay and post-process nodes are added by
    /// the owner via <see cref="Use"/>.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="sceneLayout">The attachment layout of the scene render texture
    /// (e.g. <see cref="RenderingSystem.PreferredHDRPass"/> for an HDR forward
    /// scene with its own depth attachment).
    /// Stays owned by the caller.</param>
    /// <param name="blitShader">The shader the final blit uses for the plain copy.</param>
    /// <param name="width">The initial scene texture width in pixels.</param>
    /// <param name="height">The initial scene texture height in pixels.</param>
    /// <param name="name">A diagnostic name prefix for the graph and its resources.</param>
    public RenderPipeline(RenderingSystem rendering, GPUAttachmentLayout sceneLayout, Shader blitShader, uint width, uint height, string name = "render_pipeline")
    {
        _sceneLayout = sceneLayout;

        // Color-only sibling of the scene layout for chain transform outputs.
        _postProcessLayout = CreatePostProcessLayout(rendering, sceneLayout);

        _graph = new RenderGraph(rendering, width, height, name);
        _chain = new RenderChain();
        _sceneResource = _graph.CreateTransient(new RenderGraphTextureDescriptor(
            sceneLayout, name: name + "_scene"));

        // The first color attachment is cleared to the pipeline clear color; extra
        // color attachments (e.g. a deferred position/g-buffer slot) must be cleared
        // too — leftover content would be sampled as valid data by later passes.
        ClearColorData[] clearColors = new ClearColorData[sceneLayout.Colors.Length];
        clearColors[0] = new ClearColorData(0, ColorFloat.Black);
        for (int i = 1; i < clearColors.Length; i++)
        {
            clearColors[i] = new ClearColorData((uint)i, ColorFloat.Transparent);
        }

        _clearNode = new RGNode_Clear(_sceneResource, clearColors, clearDepth: 1.0f);
        _blitNode = new RGNode_Blit(rendering, _graph, _chain, blitShader);

        _graph.Use(_clearNode);
        _graph.Use(_blitNode);
    }

    /// <summary>
    /// Creates a shell over an already-composed graph: the graph, its scene color
    /// resource, content chain and final blit were built by the caller (a preset
    /// factory — see <see cref="RenderPipelines"/>) and are adopted by the shell,
    /// which owns and disposes the graph (and through it the nodes and transients)
    /// from this point on. There is no clear node; the composed passes clear their
    /// own targets.
    /// </summary>
    internal RenderPipeline(RenderingSystem rendering, RenderGraph graph, RenderGraphTexture sceneColor, RenderChain chain, RGNode_Blit finalBlit)
    {
        _graph = graph;
        _chain = chain;
        _sceneResource = sceneColor;
        _sceneLayout = sceneColor.Layout!;
        _clearNode = null;
        _blitNode = finalBlit;
        _postProcessLayout = CreatePostProcessLayout(rendering, _sceneLayout);
    }

    /// <summary>The color-only sibling layout of a scene layout, for the output
    /// transients of chain transform nodes (post-process effects).</summary>
    private static GPUAttachmentLayout CreatePostProcessLayout(RenderingSystem rendering, GPUAttachmentLayout sceneLayout)
    {
        return rendering.GraphicsDevice.CreateAttachmentLayout(new AttachmentLayoutDescriptor(
            [new ColorAttachment(sceneLayout.Colors[0].Format)],
            null,
            "render_pipeline_post_process"));
    }

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
    /// <exception cref="InvalidOperationException">The pipeline was composed without a
    /// clear node (e.g. by a preset whose passes clear their own targets).</exception>
    public ColorFloat ClearColor
    {
        get => (_clearNode ?? throw new InvalidOperationException("This pipeline has no clear node.")).ClearColor;
        set
        {
            if (_clearNode == null)
            {
                throw new InvalidOperationException("This pipeline has no clear node.");
            }
            _clearNode.ClearColor = value;
        }
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
    /// Renders the frame through the graph: the composed nodes produce the scene
    /// content, chain transforms process it, and the final blit lands the image in
    /// <paramref name="destination"/>. Disabled nodes and unconsumed work are culled
    /// by the graph automatically.
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
