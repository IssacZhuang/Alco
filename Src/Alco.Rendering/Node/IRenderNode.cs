namespace Alco.Rendering;

/// <summary>
/// The base interface of all render nodes, the units a render pipeline orchestrates.
/// Nodes are plain objects owned by the pipeline or graph that registers them.
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
