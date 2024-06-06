using Vocore.IO;

namespace Vocore.Engine;

public class PluginDefaultAssets : BaseEnginePlugin
{
    public override int Order => -1000;
    public override void OnPostInitialize(GameEngine engine)
    {
        engine.Assets.AddFileSource(new DirectoryFileSource("Assets"));
    }

}