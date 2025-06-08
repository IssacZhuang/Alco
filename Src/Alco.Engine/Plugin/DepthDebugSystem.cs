
using System.Text;
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

    public override int Order => 2000;

    public DepthDebugSystem(GameEngine engine, ViewRenderTarget renderTarget)
    {
        _renderTarget = renderTarget;

        RenderingSystem rendering = engine.RenderingSystem;

        _renderer = rendering.CreateRenderContext("blit_depth_buffer");

        if (!engine.AssetSystem.TryLoadRaw(BuiltInAssetsPath.Shader_BlitDepth, out SafeMemoryHandle shaderContent))
        {
            throw new Exception("Failed to load shader");
        }

        string shaderText = Encoding.UTF8.GetString(shaderContent.AsReadOnlySpan());

        var bindingLayout = new BindGroupLayout[]{
            new BindGroupLayout{
                Group = 0,
                Bindings = new BindGroupEntryInfo[]{
                    new BindGroupEntryInfo{
                        Entry = new BindGroupEntry(0, ShaderStage.Standard, BindingType.Texture, TextureBindingInfo.Depth2D),
                        Size = 0
                    },
                    new BindGroupEntryInfo{
                        Entry = new BindGroupEntry(1, ShaderStage.Standard, BindingType.Sampler),
                        Size = 0
                    },
                }
            }
        };

        Shader shader = rendering.CreateShader(shaderText, BuiltInAssetsPath.Shader_BlitDepth, null, bindingLayout);

        _mesh = rendering.MeshFullScreen;

        _material = shader.CreateMaterial();
        _material.SetRenderTextureDepth(ShaderResourceId.Texture, renderTarget.RenderTexture);


    }

    public override void OnPostUpdate(float delta)
    {
        _renderer.Begin(_renderTarget.FrameBuffer);
        _renderer.Draw(_mesh, _material);
        _renderer.End();
    }

    public override void Dispose()
    {

    }
}