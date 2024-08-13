using System;
using Vocore.Graphics;

namespace Vocore.Engine;
public class PluginRuntimeInfo : BaseEnginePlugin
{
    public override int Order => -1000;

    public override void OnPostInitialize(GameEngine engine)
    {
        GPUDevice device = engine.GraphicsDevice;

        Log.Info("\n--- Thread ---");
        Log.Info("CPU Thread Count: \t" + Environment.ProcessorCount);
        Log.Info("Main Thread Id\t" + engine.MainThread);
    }

    public override void Dispose()
    {

    }
}
