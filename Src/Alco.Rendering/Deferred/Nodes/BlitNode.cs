using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The final node of the <see cref="PBRDeferredPipeline"/>'s graph: copies the
/// resource at the tail of the post chain into the frame's destination with a
/// full-screen draw. Always registered last (the pipeline re-inserts it after every
/// dynamically added chain node). Disabled on headless frames (null destination);
/// otherwise it is the graph's culling root
/// (<see cref="RenderGraphBuilder.ProducesOutput"/>).
/// </summary>
internal sealed class BlitNode : AutoDisposable, IRenderGraphNode
{
    private readonly PBRDeferredPipeline _pipeline;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Material _blitMaterial;

    // The resource to blit, captured during Setup (it is the post chain tail by the
    // time this node — registered last — runs its Setup).
    private RenderGraphTexture? _input;

    internal BlitNode(PBRDeferredPipeline pipeline, Shader blitShader)
    {
        _pipeline = pipeline;
        RenderingSystem rendering = pipeline.Rendering;
        _renderContext = rendering.CreateRenderContext();
        _fullScreenMesh = rendering.MeshFullScreen;
        _blitMaterial = rendering.CreateMaterial(blitShader);
    }

    /// <inheritdoc />
    public bool IsEnabled => !_pipeline.FrameDestinationNull;

    /// <inheritdoc />
    public void Setup(RenderGraphBuilder builder)
    {
        _input = _pipeline.PostChain.Current!;
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
