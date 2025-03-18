
using Alco.Engine;
using Alco.Rendering;

namespace Alco.ImGUI;

public class PluginImGUI: BaseEnginePlugin
{
    public override void OnPostInitialize(GameEngine engine)
    {
        ImGUISystem imGUISystem = new ImGUISystem(engine);
        engine.AddSystem(imGUISystem);
    }

    public override void Dispose()
    {
        
    }
}
