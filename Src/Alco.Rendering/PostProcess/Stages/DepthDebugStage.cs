
using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Post-process stage that visualizes the scene depth buffer as grayscale, replacing the
/// final image. Debug tooling only. Samples <see cref="PostProcessContext.SceneSource"/>
/// depth so it works from any position in the chain.
/// </summary>
public sealed class DepthDebugStage : PostProcessStage
{
    private struct Data
    {
        public Vector2 CanvasSize;
        public Vector2 DynamicRange;
    }

    private readonly RenderingSystem _rendering;
    private readonly RenderContext _renderContext;
    private readonly Mesh _fullScreenMesh;
    private readonly Shader _blitDepthShader;
    private readonly Material _materialBlitToTmp;
    private readonly Material _materialBlitToDestination;
    private readonly GraphicsValueBuffer<Data> _dataBuffer;

    // The depth texture cannot be a depth attachment and a sampled source in the same
    // pass, so the depth visualization goes through a temporary texture.
    private RenderTexture _tmpTexture;
    private RenderTexture? _boundScene;

    /// <inheritdoc />
    public override int Order => 2000;

    /// <summary>
    /// The dynamic range for depth normalization. X maps to black, Y maps to white.
    /// </summary>
    public Vector2 DynamicRange { get; set; } = new Vector2(0.0f, 1.0f);

    /// <summary>
    /// Creates the stage.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="blitDepthShaderText">The source text of the depth blit shader.</param>
    /// <param name="blitDepthShaderName">The name of the depth blit shader.</param>
    /// <param name="blitShader">The plain blit shader. Stays owned by the caller.</param>
    /// <param name="width">The initial width in pixels.</param>
    /// <param name="height">The initial height in pixels.</param>
    public DepthDebugStage(RenderingSystem rendering, string blitDepthShaderText, string blitDepthShaderName, Shader blitShader, uint width, uint height)
    {
        _rendering = rendering;
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
    }

    /// <inheritdoc />
    public override void Apply(PostProcessContext context)
    {
        if (!context.SceneSource.HasDepth)
        {
            context.Chain.Blit(context.Source, context.Destination);
            return;
        }

        if (!ReferenceEquals(_boundScene, context.SceneSource))
        {
            _materialBlitToTmp.SetRenderTextureDepth(ShaderResourceId.Texture, context.SceneSource);
            _boundScene = context.SceneSource;
        }

        _dataBuffer.Value.CanvasSize = new Vector2(context.SceneSource.Width, context.SceneSource.Height);
        _dataBuffer.Value.DynamicRange = DynamicRange;
        _dataBuffer.UpdateBuffer();

        _renderContext.Begin(_tmpTexture.FrameBuffer);
        _renderContext.Draw(_fullScreenMesh, _materialBlitToTmp);
        _renderContext.End();

        _renderContext.Begin(context.Destination);
        _renderContext.Draw(_fullScreenMesh, _materialBlitToDestination);
        _renderContext.End();
    }

    /// <inheritdoc />
    public override void Resize(uint width, uint height)
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
            _blitDepthShader.Dispose();
            _renderContext.Dispose();
        }
    }
}
