using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Base class for graph nodes that draw content in place into the chain's current target
/// without advancing the chain. Derived types implement <see cref="OnRender"/> and own their
/// render pass.
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
        OnRender(context, target.FrameBuffer, target.AttachmentLayout);
    }

    /// <summary>
    /// Renders the node's content into <paramref name="target"/>.
    /// </summary>
    /// <param name="context">The frame execution context; open the pass on
    /// <see cref="RenderGraphContext.RenderContext"/>.</param>
    /// <param name="target">The frame buffer assigned by the chain: the pipeline's
    /// content texture or, once a transform node has run, a chain-owned temporary
    /// holding the content produced so far.</param>
    /// <param name="layout">The attachment layout of <paramref name="target"/>, for
    /// material compatibility and render bundle recording.</param>
    protected abstract void OnRender(in RenderGraphContext context, GPUFrameBuffer target, GPUAttachmentLayout layout);

    /// <inheritdoc />
    public virtual void Resize(uint width, uint height) { }

    /// <inheritdoc />
    protected override void Dispose(bool disposing) { }
}
