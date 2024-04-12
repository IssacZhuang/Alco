namespace Vocore.Engine;

public class PluginDefaultAssets : IEnginePlugin
{
    public int Order => -1000;
    public void OnInitilize(GameEngine engine)
    {
        engine.Assets.AddFileSource(new DirectoryFileSource("Assets"));
    }

    public void Dispose()
    {

    }
}