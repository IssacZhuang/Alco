
using Vocore.Graphics;
using Vocore.Rendering;
using Vocore.IO;

namespace Vocore.Engine;

public class BloomSystem : BaseEngineSystem
{

    private readonly RenderingSystem _rendering;
    private readonly Shader _blitShader;
    private readonly Shader _clampShader;
    private readonly Shader _downSampleShader;
    private readonly Shader _upSampleShader;
    private readonly Bloom _bloom;

    public override int Order => 900;

    public BloomSystem(GameEngine engine)
    {
        RenderingSystem rendering = engine.Rendering;
        AssetSystem assets = engine.Assets;

        _rendering = rendering;

        _clampShader = assets.Load<Shader>("Rendering/Shader/PostProcess/Bloom/Clamp.hlsl");
        _downSampleShader = assets.Load<Shader>("Rendering/Shader/PostProcess/Bloom/DownSample.hlsl");
        _upSampleShader = assets.Load<Shader>("Rendering/Shader/PostProcess/Bloom/UpSample.hlsl");
        _blitShader = assets.Load<Shader>("Rendering/Shader/PostProcess/Bloom/Blit.hlsl");
        _bloom = rendering.CreateBloom(_blitShader, _clampShader, _downSampleShader, _upSampleShader, 11);
        _bloom.SetInput(rendering.DefaultFrameBuffer);
    }

    public override void OnPostUpdate(float delta)
    {
        _bloom.Blit(_rendering.DefaultFrameBuffer);
    }

    public override void OnResize(int2 size)
    {
        _bloom.SetInput(_rendering.DefaultFrameBuffer);
    }

    public override void Dispose()
    {
        _bloom.Dispose();
        _clampShader.Dispose();
        _downSampleShader.Dispose();
        _blitShader.Dispose();
        GC.SuppressFinalize(this);
    }
}