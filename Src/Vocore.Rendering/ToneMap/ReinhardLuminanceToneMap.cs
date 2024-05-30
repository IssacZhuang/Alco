using Vocore.Graphics;

namespace Vocore.Rendering;

public class ReinhardLuminanceToneMap : ToneMap
{
    private readonly GraphicsValueBuffer<ReinhardToneMapData> _data;
    private readonly uint _shaderId_data;


    public ref ReinhardToneMapData Data
    {
        get => ref _data.Value;
    }
    public ReinhardLuminanceToneMap(RenderingSystem renderingSystem, Shader toneMapShader) : base(renderingSystem, toneMapShader)
    {
        _data = renderingSystem.CreateGraphicsValueBuffer<ReinhardToneMapData>("reinhard_luminance_tone_map_data");
        _shaderId_data = toneMapShader.GetResourceId("data");
        _data.Value = ReinhardToneMapData.Default;
    }

    protected override void OnSetGraphicsResources(GPUCommandBuffer command)
    {
        _data.UpdateBuffer();

        command.SetGraphicsResources(_shaderId_data, _data.EntryReadonly);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        //dispose private managed resources
        _data.Dispose();
    }
}