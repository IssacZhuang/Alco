
using System.Numerics;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

public class DepthDebugSystem : BaseEngineSystem
{
    private readonly Material _material;
    private readonly Mesh _mesh;
    private readonly RenderContext _renderer;
    private readonly ViewRenderTarget _renderTarget;

    private readonly GraphicsValueBuffer<Vector2> _canvasSizeBuffer;

    public override int Order => 2000;

    public DepthDebugSystem(GameEngine engine, ViewRenderTarget renderTarget)
    {
        _renderTarget = renderTarget;

        RenderingSystem rendering = engine.RenderingSystem;

        _renderer = rendering.CreateRenderContext("blit_depth_buffer");

        _canvasSizeBuffer = rendering.CreateGraphicsValueBuffer<Vector2>();

        AssetSystem assetSystem = engine.AssetSystem;

        string shaderText = assetSystem.Load<string>(BuiltInAssetsPath.Shader_BlitDepth);

        Shader shader = rendering.CreateShader(
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

        _mesh = rendering.MeshFullScreen;

        _material = shader.CreateMaterial();
        _material.SetRenderTextureDepth(ShaderResourceId.Texture, renderTarget.RenderTexture);

        _material.SetBuffer(ShaderResourceId.Data, _canvasSizeBuffer);

        _renderTarget.OnResize += OnWindowResize;
    }

    public override void OnPostUpdate(float delta)
    {
        _renderer.Begin(_renderTarget.FrameBuffer);
        _renderer.Draw(_mesh, _material);
        _renderer.End();
    }

    private void OnWindowResize(uint2 size)
    {
        _canvasSizeBuffer.UpdateBuffer(new Vector2(size.X, size.Y));
    }

    public override void Dispose()
    {
        _renderTarget.OnResize -= OnWindowResize;
    }
}