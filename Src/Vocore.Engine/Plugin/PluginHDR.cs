using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

public class PluginHDR : BaseEnginePlugin
{
    private Shader? _shader;
    private Material? _material;
    private ReinhardToneMapData _data;

    public override int Order => -900;
    public override void OnPostInitialize(GameEngine engine)
    {
        _data = ReinhardToneMapData.Default;
        RenderingSystem rendering = engine.Rendering;
        _shader = engine.Assets.Load<Shader>(BuiltInAssetsPath.Shader_ReinhardLuminanceTonemap);
        _material = rendering.CreateGraphicsMaterial(_shader);
        _material.SetValue(ShaderResourceId.Data, _data);

        
        engine.MainRenderTarget.SetRenderPass(rendering.PrefferedHDRPass, _material);
    }

    public override void Dispose()
    {
        _shader?.Dispose();
        _material?.Dispose();

    }
}