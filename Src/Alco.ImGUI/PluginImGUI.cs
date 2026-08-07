
using Alco.Engine;

namespace Alco.ImGUI;

public class PluginImGUI: BaseEnginePlugin
{
    public override int Order => 2100;

    public override void OnPostInitialize(GameEngine engine)
    {
        engine.AddSystem(new ImGUISystem(engine));
    }
}
