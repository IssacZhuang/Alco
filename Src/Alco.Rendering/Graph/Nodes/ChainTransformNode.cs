using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Base class for chain transform nodes: post-process effects that read the chain's
/// current content and write the transformed result into their own private transient
/// output, then advance the chain to that output. On headless frames (null
/// destination) the node is culled automatically — its output is never consumed.
/// <br/>Derive and implement <see cref="OnProcess"/>; both textures are backed by
/// real GPU textures for the duration of the call. The node owns its output
/// transient — destroyed via <see cref="RenderGraph.DestroyTransient"/> with the
/// node — and the graph's transient pool aliases the historical ping-pong
/// temporaries.
/// </summary>
public abstract class ChainTransformNode : AutoDisposable, IRenderGraphNode
{
    private readonly RenderGraph _graph;

    // The resource read this frame, captured during Setup (the chain continues to
    // advance for later nodes before Execute runs).
    private RenderGraphTexture? _input;

    /// <summary>
    /// Creates the node, including its private output transient.
    /// </summary>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain the node reads and advances.</param>
    /// <param name="outputLayout">The attachment layout of the output transient
    /// (typically color-only, in the chain's content format).</param>
    /// <param name="resolutionScale">The output's resolution scale relative to the
    /// graph viewport.</param>
    /// <param name="name">A diagnostic name for the output transient.</param>
    protected ChainTransformNode(RenderGraph graph, RenderChain chain, GPUAttachmentLayout outputLayout,
        float resolutionScale = 1.0f, string name = "chain_transform")
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(outputLayout);
        _graph = graph;
        Chain = chain;
        Output = graph.CreateTransient(new RenderGraphTextureDescriptor(
            outputLayout, resolutionScale: resolutionScale, name: name + "_output"));
    }

    /// <summary>The content chain the node reads and advances.</summary>
    protected RenderChain Chain { get; }

    /// <summary>The resource read this frame (valid during <see cref="OnProcess"/>).</summary>
    protected RenderGraphTexture Input => _input!;

    /// <summary>The node's private output transient, destroyed with the node.</summary>
    public RenderGraphTexture Output { get; }

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

    /// <inheritdoc />
    public virtual void Resize(uint width, uint height) { }

    /// <inheritdoc />
    public virtual void Setup(RenderGraphBuilder builder)
    {
        _input = Chain.Current!;
        builder.Read(_input);
        builder.Write(Output);
        Chain.Advance(Output);
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        OnProcess(_input!.Texture, Output.Texture, context);
    }

    /// <summary>
    /// Renders the processed content of <paramref name="input"/> into
    /// <paramref name="output"/>. The two textures are always distinct.
    /// </summary>
    /// <param name="input">The texture holding the content produced so far.</param>
    /// <param name="output">The texture to write the processed content into.</param>
    /// <param name="context">The per-frame execution context.</param>
    protected abstract void OnProcess(RenderTexture input, RenderTexture output, in RenderGraphContext context);

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_graph.IsDisposed)
        {
            _graph.DestroyTransient(Output);
        }
    }
}
