using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

public class PluginHDR : BaseEnginePlugin
{
    private Shader? _shader;
    private ColorSpaceConvertRenderer? _toneMap;

    public override int Order => -900;
    public override void OnPostInitialize(GameEngine engine)
    {
        RenderingSystem rendering = engine.Rendering;
        _shader = engine.Assets.Load<Shader>("Rendering/Shader/ToneMap/ReinhardLuminanceTonemap.hlsl");
        _toneMap = rendering.CreateReinhardLuminanceToneMap(_shader);

        rendering.SetMainRenderPass(rendering.PrefferedHDRPass, _toneMap);
    }

    public override void Dispose()
    {
        _shader?.Dispose();
        _toneMap?.Dispose();

    }
}