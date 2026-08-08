namespace Alco.Rendering;

/// <summary>
/// The base interface of all render nodes — the units a render pipeline orchestrates.
/// A node renders content at a specific point of a pipeline: into a forward target
/// (<see cref="IForwardRenderNode"/>), into the G-buffer pass of a deferred pipeline
/// (<see cref="IGBufferRenderNode"/>) or into its shadow pass
/// (<see cref="IShadowRenderNode"/>).
/// <br/>Nodes are plain objects registered on a pipeline via <c>Use</c>; the pipeline
/// decides when and into what they render, and takes ownership: nodes that implement
/// <see cref="System.IDisposable"/> are disposed with the pipeline.
/// </summary>
public interface IRenderNode
{
    /// <summary>
    /// Whether the node participates in the frame. Disabled nodes are skipped entirely.
    /// The default is enabled; implementers typically override with a settable property.
    /// </summary>
    bool IsEnabled => true;

    /// <summary>
    /// Recreates resolution-dependent resources after the pipeline was resized.
    /// The default implementation does nothing.
    /// </summary>
    void Resize(uint width, uint height) { }
}
