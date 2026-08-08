
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Content processor node that visualizes the pipeline's scene depth buffer as grayscale,
/// replacing the final image. Debug tooling only. Samples the scene texture's depth
/// (not the chain input), so it works from any position in the chain.
/// </summary>
public sealed class RenderNode_DepthDebug : AutoDisposable, IContentProcessorNode
{
    private struct Data
    {
        public Vector2 CanvasSize;
        public Vector2 DynamicRange;
    }

    private readonly RenderingSystem _rendering;
    private readonly ForwardPipeline _pipeline;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Shader _blitDepthShader;
    private readonly Material _materialBlitToTmp;
    private readonly Material _materialBlitToDestination;
    private readonly Material _materialFallback;
    private readonly GraphicsValueBuffer<Data> _dataBuffer;

    // The depth texture cannot be a depth attachment and a sampled source in the same
    // pass, so the depth visualization goes through a temporary texture.
    private RenderTexture _tmpTexture;
    private RenderTexture? _boundScene;

    /// <inheritdoc />
    public bool IsEnabled { get; set; } = true;

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
    /// <param name="width">The initial width in pixels.</param>
    /// <param name="height">The initial height in pixels.</param>
    public RenderNode_DepthDebug(RenderingSystem rendering, ForwardPipeline pipeline, string blitDepthShaderText, string blitDepthShaderName, Shader blitShader, uint width, uint height)
    {
        _rendering = rendering;
        _pipeline = pipeline;
        _renderContext = rendering.CreateRenderContext("blit_depth_buffer");
        _fullScreenMesh = rendering.MeshFullScreen;

        _tmpTexture = CreateTmpTexture(width, height);

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
        _materialBlitToDestination.SetRenderTexture(ShaderResourceId.Texture, _tmpTexture);

        _materialFallback = blitShader.CreateMaterial("material_blit_depth_fallback");
    }

    /// <inheritdoc />
    public void OnRenderForward(RenderTexture input, RenderTexture target)
    {
        RenderTexture scene = _pipeline.SceneTexture;
        if (!scene.HasDepth)
        {
            _materialFallback.SetRenderTexture(ShaderResourceId.Texture, input);
            _renderContext.Begin(target.FrameBuffer);
            _renderContext.Draw(_fullScreenMesh, _materialFallback);
            _renderContext.End();
            return;
        }

        if (!ReferenceEquals(_boundScene, scene))
        {
            _materialBlitToTmp.SetRenderTextureDepth(ShaderResourceId.Texture, scene);
            _boundScene = scene;
        }

        _dataBuffer.Value.CanvasSize = new Vector2(scene.Width, scene.Height);
        _dataBuffer.Value.DynamicRange = DynamicRange;
        _dataBuffer.UpdateBuffer();

        _renderContext.Begin(_tmpTexture.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _materialBlitToTmp);
        _renderContext.End();

        _renderContext.Begin(target.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _materialBlitToDestination);
        _renderContext.End();
    }

    /// <inheritdoc />
    public void Resize(uint width, uint height)
    {
        if (_tmpTexture.Width == width && _tmpTexture.Height == height)
        {
            return;
        }

        _tmpTexture.Dispose();
        _tmpTexture = CreateTmpTexture(width, height);
        _materialBlitToDestination.SetRenderTexture(ShaderResourceId.Texture, _tmpTexture);
    }

    private RenderTexture CreateTmpTexture(uint width, uint height)
    {
        return _rendering.CreateRenderTexture(
            _rendering.PreferredSDRPassWithoutDepth,
            width,
            height,
            "tmp_depth_texture");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tmpTexture.Dispose();
            _dataBuffer.Dispose();
            _materialBlitToTmp.Dispose();
            _materialBlitToDestination.Dispose();
            _materialFallback.Dispose();
            _blitDepthShader.Dispose();
            _renderContext.Dispose();
        }
    }
}
