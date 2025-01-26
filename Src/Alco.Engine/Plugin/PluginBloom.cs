namespace Alco.Engine;

public class PluginBloom : BaseEnginePlugin
{
    private BloomSystem? _bloomSystem;

    public override int Order => 900;
    public override void OnPostInitialize(GameEngine engine)
    {
        _bloomSystem = new BloomSystem(engine, engine.MainRenderTarget);
        engine.AddSystem(_bloomSystem);
    }

    public override void Dispose()
    {
        _bloomSystem?.Dispose();
    }
}