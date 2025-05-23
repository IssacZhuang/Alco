using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

public class PluginHDR : BaseEnginePlugin
{
    private Shader? _shader;
    private Material? _material;
    private GraphicsValueBuffer<ReinhardToneMapData>? _dataBuffer;
    private ReinhardToneMapData _data;

    public override int Order => -900;

    public float MaxLuminance { 
        get => _data.MaxLuminance;
        set
        {
            _data.MaxLuminance = value;
            _dataBuffer?.UpdateBuffer();
        }
    }

    public float Gamma { 
        get => _data.Gamma;
        set
        {
            _data.Gamma = value;
            _dataBuffer?.UpdateBuffer();
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

        _dataBuffer = rendering.CreateGraphicsValueBuffer(_data, "hdr_tonemap_data");
        _material.SetBuffer(ShaderResourceId.Data, _dataBuffer);
        
        engine.MainRenderTarget.SetRenderPass(rendering.PrefferedHDRPass, _material);
    }

    public override void Dispose()
    {
        _shader?.Dispose();
        _material?.Dispose();
        _dataBuffer?.Dispose();
    }
}