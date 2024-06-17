using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

public class PluginSDR : BaseEnginePlugin
{
    private Shader? _shader;
    private ColorSpaceConverter? _converter;

    public override int Order => -900;
    public override void OnPostInitialize(GameEngine engine)
    {
        RenderingSystem rendering = engine.Rendering;
        _shader = engine.Assets.Load<Shader>(BuiltInAssetsPath.Shader_Blit);
        _converter = rendering.CreateColorSpaceConverter(_shader);

        rendering.SetMainRenderPass(rendering.PrefferedSDRPass, _converter);
    }

    public override void Dispose()
    {
        _shader?.Dispose();
        _converter?.Dispose();

    }
}