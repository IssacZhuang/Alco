namespace Alco.Rendering;

/// <summary>
/// A graph node that invokes a delegate: per-frame data uploads, event hooks between
/// passes, or any GPU work that does not fit the other building blocks. Declares no
/// resources by default; it can root the graph (see <see cref="ProducesGraphOutput"/>).
/// For work that reads or writes graph resources, implement
/// <see cref="IRenderGraphNode"/> directly instead.
/// </summary>
public sealed class CallbackNode : IRenderGraphNode
{
    /// <summary>
    /// The delegate invoked in <see cref="Execute"/>. The node is disabled while null.
    /// </summary>
    public Action<RenderGraphContext>? Callback { get; set; }

    /// <summary>
    /// Whether the node declares <see cref="RenderGraphBuilder.ProducesOutput"/> in
    /// its setup, making it a culling root. The default is true — a callback's side
    /// effects are invisible to the graph, so it must not be culled.
    /// </summary>
    public bool ProducesGraphOutput { get; set; } = true;

    /// <inheritdoc />
    public bool IsEnabled => Callback != null;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        if (ProducesGraphOutput)
        {
            builder.ProducesOutput();
        }
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        Callback?.Invoke(context);
    }
}
