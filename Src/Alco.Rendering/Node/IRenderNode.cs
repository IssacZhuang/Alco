namespace Alco.Rendering;

/// <summary>
/// The base interface of all render nodes — the units a render pipeline orchestrates.
/// A node renders content at a specific point of a pipeline: as a
/// <see cref="IRenderGraphNode"/> scheduled by a <see cref="RenderGraph"/>, or scoped
/// inside a pass node (<see cref="IRenderPassContent"/>,
/// <see cref="IShadowPassContent"/>).
/// <br/>Nodes are plain objects registered on a pipeline or graph via <c>Use</c>; the
/// owner decides when and into what they render, and takes ownership: nodes that
/// implement <see cref="System.IDisposable"/> are disposed with the owner.
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
