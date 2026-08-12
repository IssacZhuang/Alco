
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Chain transform node that visualizes the pipeline's scene depth buffer as grayscale,
/// replacing the final image. Debug tooling only. Samples the scene texture's depth
/// (not the chain input), so it works from any position in the chain.
/// </summary>
public sealed class RGNode_DepthDebug : RGNode_ChainTransform
{
    private struct Data
    {
        public Vector2 CanvasSize;
        public Vector2 DynamicRange;
    }

    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Shader _blitDepthShader;
    private readonly Material _materialBlitToTmp;
    private readonly Material _materialBlitToDestination;
    private readonly Material _materialFallback;
    private readonly GraphicsValueBuffer<Data> _dataBuffer;
    private readonly RenderGraphTexture _sceneResource;
    private readonly RenderGraphTexture _tmpResource;
    private readonly RenderGraph _graph;

    /// <summary>
    /// The dynamic range for depth normalization. X maps to black, Y maps to white.
    /// </summary>
    public Vector2 DynamicRange { get; set; } = new Vector2(0.0f, 1.0f);

    /// <summary>
    /// Creates the node.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="pipeline">The pipeline whose scene texture depth is visualized.</param>
    /// <param name="blitDepthShaderText">The source text of the depth blit shader.</param>
    /// <param name="blitDepthShaderName">The name of the depth blit shader.</param>
    /// <param name="blitShader">The plain blit shader. Stays owned by the caller.</param>
    public RGNode_DepthDebug(RenderingSystem rendering, ForwardPipeline pipeline, string blitDepthShaderText, string blitDepthShaderName, Shader blitShader)
        : base(pipeline.Graph, pipeline.Chain, pipeline.PostProcessLayout, name: "depth_debug")
    {
        _graph = pipeline.Graph;
        _renderContext = rendering.CreateRenderContext("blit_depth_buffer");
        _fullScreenMesh = rendering.MeshFullScreen;
        _sceneResource = pipeline.SceneColorResource;

        // The depth texture cannot be a depth attachment and a sampled source in the
        // same pass, so the depth visualization goes through a temporary texture.
        _tmpResource = pipeline.Graph.CreateTransient(new RenderGraphTextureDescriptor(
            rendering.PreferredSDRPassWithoutDepth, name: "depth_debug_tmp"));

        _dataBuffer = rendering.CreateGraphicsValueBuffer<Data>();

        _blitDepthShader = rendering.CreateShader(
            blitDepthShaderText,
            blitDepthShaderName,
            null,
            new BindGroupLayout[]{
                new BindGroupLayout(){
                    Group=0,
                    Bindings = [
                        new BindGroupEntryInfo(){
                            Entry = new BindGroupEntry(
                                0,
                                ShaderStage.Standard,
                                BindingType.Texture,
                                TextureBindingInfo.Depth2D
                                )
                        },
                    ]
                },
                new BindGroupLayout(){
                    Group=1,
                    Bindings = [
                        new BindGroupEntryInfo(){
                            Entry = new BindGroupEntry(
                                0,
                                ShaderStage.Standard,
                                BindingType.UniformBuffer)
                        }
                    ]
                }
            }
        );

        _materialBlitToTmp = _blitDepthShader.CreateMaterial("material_blit_to_tmp");
        _materialBlitToTmp.SetBuffer(ShaderResourceId.Data, _dataBuffer);

        _materialBlitToDestination = blitShader.CreateMaterial("material_blit_to_destination");
        _materialBlitToDestination.SetRenderTexture(ShaderResourceId.Texture, _tmpResource.Texture);

        _materialFallback = blitShader.CreateMaterial("material_blit_depth_fallback");
    }

    /// <inheritdoc />
    public override void Setup(RenderGraphBuilder builder)
    {
        base.Setup(builder);
        // The scene depth is sampled regardless of the chain's current position.
        builder.Read(_sceneResource);
        builder.Write(_tmpResource);
    }

    /// <inheritdoc />
    protected override void OnProcess(RenderTexture input, RenderTexture output, in RenderGraphContext context)
    {
        RenderTexture scene = _sceneResource.Texture;
        if (!scene.HasDepth)
        {
            _materialFallback.SetRenderTexture(ShaderResourceId.Texture, input);
            _renderContext.Begin(output.FrameBuffer);
            _renderContext.Draw(_fullScreenMesh, _materialFallback);
            _renderContext.End();
            return;
        }

        // The scene facade identity is stable: the depth binding is refreshed by the
        // material system's version check after a resize.
        _materialBlitToTmp.SetRenderTextureDepth(ShaderResourceId.Texture, scene);

        _dataBuffer.Value.CanvasSize = new Vector2(scene.Width, scene.Height);
        _dataBuffer.Value.DynamicRange = DynamicRange;
        _dataBuffer.UpdateBuffer();

        _renderContext.Begin(_tmpResource.Texture.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _materialBlitToTmp);
        _renderContext.End();

        _renderContext.Begin(output.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _materialBlitToDestination);
        _renderContext.End();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dataBuffer.Dispose();
            _materialBlitToTmp.Dispose();
            _materialBlitToDestination.Dispose();
            _materialFallback.Dispose();
            _blitDepthShader.Dispose();
            _renderContext.Dispose();
            if (!_graph.IsDisposed)
            {
                _graph.DestroyTransient(_tmpResource);
            }
        }
        base.Dispose(disposing);
    }
}
