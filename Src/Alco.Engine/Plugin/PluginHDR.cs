using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Engine;

public class PluginHDR : BaseEnginePlugin
{
    private Shader? _shader;
    private Material? _material;
    private GraphicsBuffer? _dataBuffer;

    private ReinhardToneMapData _data = ReinhardToneMapData.Default;

    public override int Order => -900;

    public float MaxLuminance { 
        get => _data.MaxLuminance;
        set
        {
            _data.MaxLuminance = value;
            _dataBuffer?.UpdateBuffer(_data);
        }
    }

    public float Gamma { 
        get => _data.Gamma;
        set
        {
            _data.Gamma = value;
            _dataBuffer?.UpdateBuffer(_data);
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

    public unsafe override void OnPostInitialize(GameEngine engine)
    {
        RenderingSystem rendering = engine.RenderingSystem;
        _shader = engine.AssetSystem.Load<Shader>(BuiltInAssetsPath.Shader_ReinhardLuminanceTonemap);
        _material = rendering.CreateMaterial(_shader);

        _dataBuffer = rendering.CreateGraphicsBuffer((uint)sizeof(ReinhardToneMapData), "hdr_tonemap_data");
        _dataBuffer.UpdateBuffer(_data);
        _material.SetBuffer(ShaderResourceId.Data, _dataBuffer);

        engine.MainRenderTarget.SetAttachmentLayout(rendering.PrefferedHDRPass, _material);
    }

    public override void Dispose()
    {
        _shader?.Dispose();
        _material?.Dispose();
        _dataBuffer?.Dispose();
    }
}