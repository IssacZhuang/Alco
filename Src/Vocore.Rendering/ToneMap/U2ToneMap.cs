using Vocore.Graphics;

namespace Vocore.Rendering;

public class U2ToneMap : ToneMap
{
    private readonly GraphicsValueBuffer<U2ToneMapData> _buffer;
    private readonly uint _shaderId_buffer;
    
    internal U2ToneMap(RenderingSystem renderingSystem, Shader toneMapShader) : base(renderingSystem, toneMapShader)
    {
        _buffer = renderingSystem.CreateGraphicsValueBuffer<U2ToneMapData>("u2_tone_map_buffer");
        _shaderId_buffer = toneMapShader.GetResourceId("uncharted2Data");
        _buffer.Value = U2ToneMapData.Default;
    }

    protected override void OnSetGraphicsResources(GPUCommandBuffer command)
    {
        _buffer.UpdateBuffer();
        command.SetGraphicsResources(_shaderId_buffer, _buffer.EntryReadonly);
    }

    protected override void Dispose(bool disposing)
    {
        _buffer.Dispose();
        base.Dispose(disposing);
    }
}