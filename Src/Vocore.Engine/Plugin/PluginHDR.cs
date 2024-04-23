using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.Engine;

public class PluginHDR : BaseEnginePlugin
{
    private Shader? _shader;
    private ToneMap? _toneMap;
    private GPURenderPass? _renderPass;

    public override int Order => -900;
    public override void OnPostInitialize(GameEngine engine)
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

    public override void Dispose()
    {
        _shader?.Dispose();
        _toneMap?.Dispose();
        _renderPass?.Dispose();
    }
}