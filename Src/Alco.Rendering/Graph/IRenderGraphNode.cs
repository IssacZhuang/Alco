namespace Alco.Rendering;

/// <summary>
/// A node of a <see cref="RenderGraph"/> — the unit the graph schedules. A graph node
/// declares its own resource dependencies:
/// <list type="bullet">
/// <item><see cref="Setup"/> runs every frame, in registration order, before any
/// <see cref="Execute"/>. It declares which <see cref="RenderGraphTexture"/> resources
/// the node reads and writes this frame. It must not allocate managed memory.</item>
/// <item><see cref="Execute"/> records the GPU work, and only runs when the node
/// survived culling (see <see cref="RenderGraph"/>).</item>
/// </list>
/// A node whose writes are never consumed (directly or transitively) by a
/// <see cref="RenderGraphBuilder.ProducesOutput"/> node is culled: its
/// <see cref="Execute"/> does not run and its transient writes are never materialized.
/// <br/>Nodes are plain objects registered via <see cref="RenderGraph.Use"/>; the graph
/// takes ownership and disposes nodes implementing <see cref="System.IDisposable"/>
/// with itself.
/// </summary>
public interface IRenderGraphNode : IRenderNode
{
    /// <summary>
    /// Declares this frame's resource reads and writes. Called every frame, in
    /// registration order, before any <see cref="Execute"/>. The default implementation
    /// declares no dependencies, which keeps the node alive only when it produces
    /// output or nothing downstream needs its (empty) writes.
    /// <br/>This method is on the per-frame hot path: implementations must not
    /// allocate managed memory.
    /// </summary>
    /// <param name="builder">The dependency declaration builder, valid only during
    /// this call. Do not store it.</param>
    void Setup(RenderGraphBuilder builder) { }

    /// <summary>
    /// Records this frame's GPU work. Called only for nodes that survived culling,
    /// in registration order. Transient resources the node declared are backed by
    /// real GPU textures for the duration of this call (acquired at the node's first
    /// touch, released after its last touch).
    /// <br/>All uniform buffer uploads a pass depends on must be issued before the
    /// pass is recorded: submissions inside the graph are deferred and batched, so a
    /// buffer rewritten after recording would leak the newer value into the earlier
    /// pass.
    /// </summary>
    /// <param name="context">The per-frame execution context, valid only during
    /// this call. Do not store it.</param>
    void Execute(in RenderGraphContext context);
}
