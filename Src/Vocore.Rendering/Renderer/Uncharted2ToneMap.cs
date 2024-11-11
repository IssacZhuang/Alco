using Vocore.Graphics;

namespace Vocore.Rendering;

public class Uncharted2ToneMap : ColorSpaceConverter
{
    private readonly GraphicsValueBuffer<U2ToneMapData> _data;
    private uint _shaderId_data;

    internal Uncharted2ToneMap(RenderingSystem renderingSystem, Shader toneMapShader) : base(renderingSystem, toneMapShader)
    {
        _data = renderingSystem.CreateGraphicsValueBuffer<U2ToneMapData>("u2_tone_map_buffer");
        _data.Value = U2ToneMapData.Default;
    }

    protected override void OnSetGraphicsResources(GPUCommandBuffer command)
    {
        _data.UpdateBuffer();
        command.SetGraphicsResources(_shaderId_data, _data.EntryReadonly);
    }

    protected override void OnUpdatePipeline(GraphicsPipelineInfo pipelineInfo)
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