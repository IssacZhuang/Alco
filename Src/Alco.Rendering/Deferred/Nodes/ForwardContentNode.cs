using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Adapts one chain-registered <see cref="IForwardRenderNode"/> (transparency and
/// other forward content) into the <see cref="PBRDeferredPipeline"/>'s graph. Draws
/// in place into the resource currently holding the scene content (the scene color
/// target while no post-process node has run — including its shared G-buffer depth,
/// which is what gives glass hardware depth testing without a depth copy).
/// <br/>The node never advances the post chain; on headless frames (null
/// destination) it still runs, matching the historical chain behavior, by rooting
/// itself via <see cref="RenderGraphBuilder.ProducesOutput"/>. Enable state and
/// resize notifications are forwarded to the source node, and the adapter owns the
/// source (disposed with the graph).
/// </summary>
internal sealed class ForwardContentNode : AutoDisposable, IChainAdapterNode
{
    private readonly PBRDeferredPipeline _pipeline;
    private readonly IForwardRenderNode _source;

    // The resource this node draws into, captured during Setup (the post chain
    // continues to advance for later nodes before Execute runs).
    private RenderGraphTexture? _input;

    internal ForwardContentNode(PBRDeferredPipeline pipeline, IForwardRenderNode source)
    {
        _pipeline = pipeline;
        _source = source;
    }

    /// <inheritdoc />
    public IRenderNode Source => _source;

    /// <inheritdoc />
    public bool IsEnabled => _source.IsEnabled;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        _input = _pipeline.PostChain.Current!;
        builder.ReadWrite(_input);
        if (_pipeline.FrameDestinationNull)
        {
            builder.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        RenderTexture current = _input!.Texture;
        _source.OnRenderForward(current.FrameBuffer, current.AttachmentLayout);
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
