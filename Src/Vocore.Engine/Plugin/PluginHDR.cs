using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

public class PluginHDR : IEnginePlugin
{
    private Shader? _shader;
    private ToneMap? _toneMap;
    private GPURenderPass? _renderPass;

    public int Order => -900;

    public void OnInitilize(GameEngine engine)
    {
        _shader = engine.Assets.Load<Shader>("Rendering/Shader/ToneMap/ReinhardLuminanceTonemap.hlsl");
        _toneMap = engine.Rendering.CreateReinhardLuminanceToneMap(_shader);
        RenderPassDescriptor descriptor = new RenderPassDescriptor
        (
            [new(engine.GraphicsDevice.PrefferedHDRFormat)],
            new(PixelFormat.Depth24PlusStencil8),
            "hdr_pass"
        );
        _renderPass = engine.GraphicsDevice.CreateRenderPass(descriptor);
        engine.Rendering.SetMainRenderPass(_renderPass, _toneMap);
    }

    public void Dispose()
    {
        _shader?.Dispose();
        _toneMap?.Dispose();
        _renderPass?.Dispose();
    }
}