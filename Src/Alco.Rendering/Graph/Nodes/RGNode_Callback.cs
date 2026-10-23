namespace Alco.Rendering;

/// <summary>
/// A graph node that invokes a delegate for per-frame data uploads, event hooks or other GPU
/// work. It declares no resources and roots the graph by default.
/// </summary>
public sealed class RGNode_Callback : IRenderGraphNode
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
