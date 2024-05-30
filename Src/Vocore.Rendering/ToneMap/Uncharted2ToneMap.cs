using Vocore.Graphics;

namespace Vocore.Rendering;

public class Uncharted2ToneMap : ToneMap
{
    private readonly GraphicsValueBuffer<U2ToneMapData> _data;
    private readonly uint _shaderId_data;

    internal Uncharted2ToneMap(RenderingSystem renderingSystem, Shader toneMapShader) : base(renderingSystem, toneMapShader)
    {
        _data = renderingSystem.CreateGraphicsValueBuffer<U2ToneMapData>("u2_tone_map_buffer");
        _shaderId_data = toneMapShader.GetResourceId("data");
        _data.Value = U2ToneMapData.Default;
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