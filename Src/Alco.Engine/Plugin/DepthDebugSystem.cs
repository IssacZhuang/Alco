
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

        Shader shader = engine.BuiltInAssets.Shader_Blit;

        _mesh = rendering.MeshFullScreen;

        _material = shader.CreateMaterial();
        _material.SetRenderTextureDepth(ShaderResourceId.Texture, renderTarget.RenderTexture);


    }

    public override void OnPostUpdate(float delta)
    {
        // _renderer.Begin(_renderTarget.FrameBuffer);
        // _renderer.Draw(_mesh, _material);
        // _renderer.End();
    }

    public override void Dispose()
    {

    }
}