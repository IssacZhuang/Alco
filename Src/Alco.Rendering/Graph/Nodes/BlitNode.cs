using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The final node of a graph: copies the chain's current content into the frame's
/// destination with a full-screen draw. Register it last (or keep it last with
/// <see cref="RenderGraph.InsertBefore"/> when adding chain nodes). Disabled on
/// headless frames (null destination); otherwise it is the graph's culling root
/// (<see cref="RenderGraphBuilder.ProducesOutput"/>).
/// </summary>
public sealed class BlitNode : AutoDisposable, IRenderGraphNode
{
    private readonly RenderGraph _graph;
    private readonly RenderChain _chain;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _blitMaterial;

    // The resource to blit, captured during Setup (it is the chain tail by the time
    // this node — registered last — runs its Setup).
    private RenderGraphTexture? _input;

    /// <summary>
    /// Creates the blit node, including its blit material.
    /// </summary>
    /// <param name="rendering">The rendering system, for GPU resources.</param>
    /// <param name="graph">The graph the node is (or will be) registered in.</param>
    /// <param name="chain">The content chain whose tail is blitted.</param>
    /// <param name="blitShader">The shader used for the plain copy.</param>
    /// <param name="name">A diagnostic name for the render context.</param>
    public BlitNode(RenderingSystem rendering, RenderGraph graph, RenderChain chain, Shader blitShader, string name = "blit")
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(blitShader);
        _graph = graph;
        _chain = chain;
        _renderContext = rendering.CreateRenderContext(name);
        _fullScreenMesh = rendering.MeshFullScreen;
        _blitMaterial = rendering.CreateMaterial(blitShader);
    }

    /// <inheritdoc />
    public bool IsEnabled => _graph.HasDestinationThisFrame;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        _input = _chain.Current!;
        builder.Read(_input);
        builder.ProducesOutput();
    }

    /// <inheritdoc />
    public void Execute(in RenderGraphContext context)
    {
        _blitMaterial.SetRenderTexture(ShaderResourceId.Texture, _input!.Texture);
        _renderContext.Begin(context.Destination!);
        _renderContext.Draw(_fullScreenMesh, _blitMaterial);
        _renderContext.End();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _blitMaterial.Dispose();
            _renderContext.Dispose();
        }
    }
}
