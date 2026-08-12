namespace Alco.Rendering;

/// <summary>
/// A graph node of the <see cref="PBRDeferredPipeline"/> that adapts one chain-registered
/// render node (<see cref="IForwardRenderNode"/> or <see cref="IContentProcessorNode"/>),
/// letting the pipeline map between the caller's node and its graph adapter for
/// <see cref="PBRDeferredPipeline.Remove"/> and <see cref="PBRDeferredPipeline.Get{T}"/>.
/// </summary>
internal interface IChainAdapterNode : IRenderGraphNode
{
    /// <summary>The adapted caller-registered render node.</summary>
    IRenderNode Source { get; }
}
