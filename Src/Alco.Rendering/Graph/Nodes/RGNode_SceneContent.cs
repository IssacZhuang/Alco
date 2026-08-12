using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Base class for graph nodes that draw content in place into the chain's current
/// target: scene geometry in a forward pipeline, transparency in a deferred one
/// (hardware depth-tested against the target's own depth attachment). The node never
/// advances the chain; on headless frames (null destination) it roots itself so it
/// still runs.
/// <br/>Derive from this class and implement <see cref="OnRender"/>; the node begins
/// and ends nothing itself — the implementation owns its render pass on the given
/// target (render bundles recorded via <see cref="SubRenderContext"/> are replayed
/// into any open <see cref="RenderContext"/> via
/// <see cref="RenderContext.ExecuteSubContext"/>).
/// </summary>
public abstract class RGNode_SceneContent : AutoDisposable, IRenderGraphNode
{
    private readonly RenderGraph _graph;

    // The resource drawn into this frame, captured during Setup (the chain continues
    // to advance for later nodes before Execute runs).
    private RenderGraphTexture? _target;

    /// <summary>
    /// Creates the node.
    /// </summary>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain whose current target the node draws into.</param>
    protected RGNode_SceneContent(RenderGraph graph, RenderChain chain)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(chain);
        _graph = graph;
        Chain = chain;
    }

    /// <summary>The content chain the node draws into.</summary>
    protected RenderChain Chain { get; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        _target = Chain.Current!;
        builder.ReadWrite(_target);
        if (!_graph.HasDestinationThisFrame)
        {
            builder.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        RenderTexture target = _target!.Texture;
        OnRender(target.FrameBuffer, target.AttachmentLayout);
    }

    /// <summary>
    /// Renders the node's content into <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The frame buffer assigned by the chain: the pipeline's
    /// content texture or, once a transform node has run, a chain-owned temporary
    /// holding the content produced so far.</param>
    /// <param name="layout">The attachment layout of <paramref name="target"/>, for
    /// material compatibility and render bundle recording.</param>
    protected abstract void OnRender(GPUFrameBuffer target, GPUAttachmentLayout layout);

    /// <inheritdoc />
    public virtual void Resize(uint width, uint height) { }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) { }
}
