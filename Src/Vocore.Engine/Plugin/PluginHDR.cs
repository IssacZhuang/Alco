using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

public class PluginHDR : BaseEnginePlugin
{
    private Shader? _shader;
    private Material? _material;
    private ReinhardToneMapData _data;

    public override int Order => -900;

    public float MaxLuminance { 
        get => _data.MaxLuminance;
        set
        {
            _data.MaxLuminance = value;
            _material?.SetValue(ShaderResourceId.Data, _data);
        }
    }

    public float Gamma { 
        get => _data.Gamma;
        set
        {
            _data.Gamma = value;
            _material?.SetValue(ShaderResourceId.Data, _data);
        }
    }

    public PluginHDR()
    {
        _data = ReinhardToneMapData.Default;
    }

    public PluginHDR(float maxLuminance, float gamma)
    {
        _data = ReinhardToneMapData.Default;
        _data.MaxLuminance = maxLuminance;
        _data.Gamma = gamma;
    }

    public override void OnPostInitialize(GameEngine engine)
    {
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