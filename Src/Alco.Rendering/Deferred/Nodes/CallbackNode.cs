namespace Alco.Rendering;

/// <summary>
/// Carries the <see cref="PBRDeferredPipeline.AfterGBufferCallback"/> event into the
/// graph: runs after the G-buffer pass node and before the plugin adapter, matching
/// the pipeline's historical call order. Declares no resources; it is a culling root
/// (<see cref="RenderGraphBuilder.ProducesOutput"/>) and is only enabled while the
/// event has subscribers.
/// </summary>
internal sealed class CallbackNode : IRenderGraphNode
{
    private readonly PBRDeferredPipeline _pipeline;

    internal CallbackNode(PBRDeferredPipeline pipeline)
    {
        _pipeline = pipeline;
    }

    /// <inheritdoc />
    public bool IsEnabled => _pipeline.HasAfterGBufferCallback;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        builder.ProducesOutput();
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        _pipeline.InvokeAfterGBufferCallback();
    }
}
