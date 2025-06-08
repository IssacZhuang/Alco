
namespace Alco.Engine;

public class PluginDepthDebug : BaseEnginePlugin
{
    private DepthDebugSystem? _depthDebugSystem;

    public override int Order => 2000;

    public override void OnPostInitialize(GameEngine engine)
    {
        _depthDebugSystem = new DepthDebugSystem(engine, engine.MainRenderTarget);
        engine.AddSystem(_depthDebugSystem);
    }

    public override void Dispose()
    {
        _depthDebugSystem?.Dispose();
    }
}