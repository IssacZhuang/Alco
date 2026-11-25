using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Editor.Extensibility;

/// <summary>
/// The product of an <see cref="IPreviewPipelineFactory"/>: the <see cref="RenderGraph"/>
/// driven per frame, the optional tonemap node, clear-color callback, and any factory-owned
/// GPU resources disposed alongside the pipeline.
/// </summary>
public sealed class PreviewPipeline : IDisposable
{
    private readonly Action<ColorFloat> _setClearColor;
    private readonly IDisposable[] _ownedResources;
    private bool _disposed;

    /// <summary>Creates a preview pipeline wrapper.</summary>
    /// <param name="graph">The render graph executed each frame for the viewport.</param>
    /// <param name="chain">The render chain threading scene and post-process targets.</param>
    /// <param name="tonemap">The display-transform node, or null when the pipeline has none.</param>
    /// <param name="setClearColor">Callback updating the scene clear or background color.</param>
    /// <param name="ownedResources">Optional resources owned and disposed by this pipeline.</param>
    public PreviewPipeline(
        RenderGraph graph,
        RenderChain chain,
        RGNode_Tonemap? tonemap,
        Action<ColorFloat> setClearColor,
        params IDisposable[] ownedResources)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(setClearColor);

        Graph = graph;
        Chain = chain;
        Tonemap = tonemap;
        _setClearColor = setClearColor;
        _ownedResources = ownedResources ?? [];
    }

    /// <summary>The render graph the viewport executes per frame.</summary>
    public RenderGraph Graph { get; }

    /// <summary>The content chain threaded through the graph's passes.</summary>
    public RenderChain Chain { get; }

    /// <summary>The display-transform node, or null for pipelines without one.</summary>
    public RGNode_Tonemap? Tonemap { get; }

    /// <summary>Updates the background/clear color used by the pipeline's scene pass.</summary>
    public void SetClearColor(ColorFloat color) => _setClearColor(color);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        Graph.Dispose();
        for (int i = _ownedResources.Length - 1; i >= 0; i--)
        {
            _ownedResources[i].Dispose();
        }
    }
}
