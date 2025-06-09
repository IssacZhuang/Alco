
using System.Numerics;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

public class DepthDebugSystem : BaseEngineSystem
{
    private struct Data
    {
        public Vector2 CanvasSize;
        public float DynamicMultiplier;

        public Data(Vector2 canvasSize, float dynamicMultiplier)
        {
            CanvasSize = canvasSize;
            DynamicMultiplier = dynamicMultiplier;
        }
    }

    private readonly RenderingSystem _rendering;

    private readonly Material _materialBlitToTmp;
    private readonly Material _materialBlitToMain;
    private readonly Mesh _mesh;
    private readonly RenderContext _renderer;
    private readonly ViewRenderTarget _renderTarget;

    //the depth texture can not use as depth attachment and source of read at the same time
    //so we need to create a temporary texture to store the depth texture
    private RenderTexture _tmpTexture;

    private readonly GraphicsValueBuffer<Data> _dataBuffer;

    public bool IsEnabled { get; set; } = true;

    public float DynamicMultiplier { get; set; } = 1.0f;

    public override int Order => 2000;

    public DepthDebugSystem(GameEngine engine, ViewRenderTarget renderTarget)
    {
        _renderTarget = renderTarget;

        _rendering = engine.RenderingSystem;

        _renderer = _rendering.CreateRenderContext("blit_depth_buffer");

        _tmpTexture = _rendering.CreateRenderTexture(
            _rendering.PrefferedSDRPassWithoutDepth, 
            renderTarget.RenderTexture.Width,
            renderTarget.RenderTexture.Height,
            "tmp_depth_texture"
            );

        _dataBuffer = _rendering.CreateGraphicsValueBuffer<Data>();

        AssetSystem assetSystem = engine.AssetSystem;

        string shaderText = assetSystem.Load<string>(BuiltInAssetsPath.Shader_BlitDepth);

        Shader shaderBlitToTmp = _rendering.CreateShader(
            shaderText,
            BuiltInAssetsPath.Shader_BlitDepth,
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
                                new TextureBindingInfo(TextureViewDimension.Texture2D, TextureSampleType.UnfilterableFloat)
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

        _mesh = _rendering.MeshFullScreen;

        _materialBlitToTmp = shaderBlitToTmp.CreateMaterial("material_blit_to_tmp");
        _materialBlitToTmp.SetRenderTextureDepth(ShaderResourceId.Texture, renderTarget.RenderTexture);

        _materialBlitToTmp.SetBuffer(ShaderResourceId.Data, _dataBuffer);

        Shader shaderBlitToMain = assetSystem.Load<Shader>(BuiltInAssetsPath.Shader_Blit);
        _materialBlitToMain = shaderBlitToMain.CreateMaterial("material_blit_to_main");
        _materialBlitToMain.SetRenderTexture(ShaderResourceId.Texture, _tmpTexture);

        _renderTarget.OnResize += OnWindowResize;
    }

    public override void OnPostUpdate(float delta)
    {
        if (!IsEnabled)
        {
            return;
        }

        RenderTexture targetTexture = _renderTarget.RenderTexture;

        _dataBuffer.Value.CanvasSize = new Vector2(targetTexture.Width, targetTexture.Height);
        _dataBuffer.Value.DynamicMultiplier = DynamicMultiplier;
        _dataBuffer.UpdateBuffer();

        _renderer.Begin(_tmpTexture.FrameBuffer);
        _renderer.Draw(_mesh, _materialBlitToTmp);
        _renderer.End();

        _renderer.Begin(_renderTarget.FrameBuffer);
        _renderer.Draw(_mesh, _materialBlitToMain);
        _renderer.End();
    }

    private void OnWindowResize(uint2 size)
    {
        _tmpTexture.Dispose();
        _tmpTexture = _rendering.CreateRenderTexture(
            _rendering.PrefferedSDRPassWithoutDepth,
            _renderTarget.RenderTexture.Width,
            _renderTarget.RenderTexture.Height,
            "tmp_depth_texture"
            );
    }

    public override void Dispose()
    {
        _renderTarget.OnResize -= OnWindowResize;

        _tmpTexture.Dispose();
        _dataBuffer.Dispose();
        _materialBlitToTmp.Dispose();
        _materialBlitToMain.Dispose();
        _renderer.Dispose();
    }
}