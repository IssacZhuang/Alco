namespace Alco.Rendering;

/// <summary>
/// Adapts one chain-registered <see cref="IContentProcessorNode"/> (bloom, tonemap,
/// FXAA, ...) into the <see cref="PBRDeferredPipeline"/>'s graph. Reads the resource
/// currently holding the scene content and writes its own private transient output
/// (color-only, scene color format, graph-relative size), then advances the post
/// chain to that output — the graph's transient pool replaces the historical
/// ping-pong temporaries, and processors whose output is never consumed (e.g. on
/// headless frames) are culled automatically.
/// <br/>Enable state and resize notifications are forwarded to the source node, and
/// the adapter owns the source (disposed with the graph). When the source is removed
/// from the pipeline, the output transient is destroyed via
/// <see cref="RenderGraph.DestroyTransient"/>.
/// </summary>
internal sealed class PostProcessNode : AutoDisposable, IChainAdapterNode
{
    private readonly PBRDeferredPipeline _pipeline;
    private readonly IContentProcessorNode _source;
    private readonly RenderGraphTexture _output;

    // The resource holding the content produced so far, captured during Setup (the
    // post chain continues to advance for later nodes before Execute runs).
    private RenderGraphTexture? _input;

    internal PostProcessNode(PBRDeferredPipeline pipeline, IContentProcessorNode source)
    {
        _pipeline = pipeline;
        _source = source;
        _output = pipeline.Graph.CreateTransient(new RenderGraphTextureDescriptor(
            pipeline.PostProcessLayout,
            name: "post_" + source.GetType().Name));
    }

    /// <inheritdoc />
    public IRenderNode Source => _source;

    /// <summary>This node's private output texture, destroyed when the adapter is removed.</summary>
    internal RenderGraphTexture Output => _output;

    /// <inheritdoc />
    public bool IsEnabled => _source.IsEnabled;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        _input = _pipeline.PostChain.Current!;
        builder.Read(_input);
        builder.Write(_output);
        _pipeline.PostChain.Current = _output;
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        _source.OnRenderForward(_input!.Texture, _output.Texture);
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        _source.Resize(width, height);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && _source is System.IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
