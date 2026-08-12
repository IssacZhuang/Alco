namespace Alco.Rendering;

/// <summary>
/// The dependency declaration surface passed to <see cref="IRenderGraphNode.Setup"/>.
/// A node declares which resources it reads and writes this frame; the graph derives
/// execution dependencies, culling and transient lifetimes from these declarations.
/// <br/>The builder is a single reused instance attached to the declaring node for
/// the duration of the call. It is only valid inside <see cref="IRenderGraphNode.Setup"/>
/// — do not store it, and do not allocate while using it (Setup is on the per-frame
/// hot path).
/// </summary>
public sealed class RenderGraphBuilder
{
    private RenderGraphNodeRecord? _record;

    internal RenderGraphBuilder()
    {
    }

    /// <summary>Attaches the builder to a node's record for the duration of its Setup.</summary>
    internal void Attach(RenderGraphNodeRecord record)
    {
        _record = record;
    }

    /// <summary>Detaches the builder after a node's Setup.</summary>
    internal void Detach()
    {
        _record = null;
    }

    /// <summary>
    /// Declares that the node reads <paramref name="texture"/> this frame. The resource
    /// must be written by an enabled earlier node (transient) or imported.
    /// </summary>
    public void Read(RenderGraphTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Record.AddRead(texture);
    }

    /// <summary>
    /// Declares that the node produces (overwrites) <paramref name="texture"/> this
    /// frame. The first writer of a transient drives its acquisition.
    /// </summary>
    public void Write(RenderGraphTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Record.AddWrite(texture);
    }

    /// <summary>
    /// Declares that the node modifies <paramref name="texture"/> in place (additive
    /// blending, forward rendering over existing content, etc.) — both a read and a
    /// write.
    /// </summary>
    public void ReadWrite(RenderGraphTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);
        Record.AddRead(texture);
        Record.AddWrite(texture);
    }

    /// <summary>
    /// Declares that the node has externally visible side effects (e.g. blitting to
    /// the swapchain), making it a culling root: it survives even when no later node
    /// reads its writes. Typically called conditionally, e.g. only when the frame's
    /// destination is non-null.
    /// </summary>
    public void ProducesOutput()
    {
        Record.MarkProducesOutput();
    }

    private RenderGraphNodeRecord Record
    {
        get => _record ?? throw new InvalidOperationException(
            "RenderGraphBuilder is only valid during IRenderGraphNode.Setup.");
    }
}
