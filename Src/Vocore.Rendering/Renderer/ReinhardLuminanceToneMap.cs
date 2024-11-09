using Vocore.Graphics;

namespace Vocore.Rendering;

public class ReinhardLuminanceToneMap : ColorSpaceConverter
{
    private readonly GraphicsValueBuffer<ReinhardToneMapData> _data;
    private uint _shaderId_data;


    public ref ReinhardToneMapData Data
    {
        get => ref _data.Value;
    }
    public ReinhardLuminanceToneMap(RenderingSystem renderingSystem, Shader toneMapShader) : base(renderingSystem, toneMapShader)
    {
        _data = renderingSystem.CreateGraphicsValueBuffer<ReinhardToneMapData>("reinhard_luminance_tone_map_data");
        
        _data.Value = ReinhardToneMapData.Default;
    }

    protected override void OnSetGraphicsResources(GPUCommandBuffer command)
    {
        _data.UpdateBuffer();

        command.SetGraphicsResources(_shaderId_data, _data.EntryReadonly);
    }

    protected override void OnUpdatePipeline(ShaderPipelineInfo pipelineInfo)
    {
        _shaderId_data = pipelineInfo.GetResourceId("_data");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        //dispose private managed resources
        _data.Dispose();
    }
}