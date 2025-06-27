
using Alco.Engine;
using Alco.Rendering;

namespace Alco.ImGUI;

public class PluginImGUI: BaseEnginePlugin
{
    public override int Order => 2100;

    public override void OnPostInitialize(GameEngine engine)
    {
        ImGUISystem imGUISystem = new ImGUISystem(engine, engine.MainRenderTarget);
        engine.AddSystem(imGUISystem);
    }

    public override void Dispose()
    {
        
    }
}
