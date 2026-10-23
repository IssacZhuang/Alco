namespace Alco.Rendering;

/// <summary>
/// A scheduled unit of work in a <see cref="RenderGraph"/> that declares its resource reads and writes in <see cref="Setup"/> and records GPU work in <see cref="Execute"/>.
/// The graph owns and disposes nodes implementing <see cref="System.IDisposable"/>.
/// </summary>
public interface IRenderGraphNode : IRenderNode
{
    /// <summary>
    /// Declares this frame's resource reads and writes before any <see cref="Execute"/> runs.
    /// This runs on the per-frame hot path and must not allocate managed memory.
    /// </summary>
    /// <param name="builder">The dependency declaration builder, valid only during this call.</param>
    void Setup(RenderGraphBuilder builder) { }

    /// <summary>
    /// Records this frame's GPU work for nodes that survived culling.
    /// All uniform buffer uploads the pass depends on must be issued before it is recorded.
    /// </summary>
    /// <param name="context">The per-frame execution context, valid only during this call.</param>
    void Execute(in RenderGraphContext context);
}
