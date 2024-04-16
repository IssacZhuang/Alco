namespace Vocore.Engine;

public class PluginBloom : IEnginePlugin
{
    private BloomSystem? _bloomSystem;

    public int Order => 900;
    public void OnInitilize(GameEngine engine)
    {
        _bloomSystem = new BloomSystem(engine);
        engine.AddSystem(_bloomSystem);
    }

    public void Dispose()
    {
        _bloomSystem?.Dispose();
    }
}