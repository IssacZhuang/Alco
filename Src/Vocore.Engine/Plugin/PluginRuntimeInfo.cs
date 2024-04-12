using System;
using Silk.NET.Windowing;
using Vocore.Graphics;

namespace Vocore.Engine;
public class PluginRuntimeInfo : IEnginePlugin
{
    public int Order => -1000;

    public void OnInitilize(GameEngine engine)
    {
        GPUDevice device = engine.GraphicsDevice;

        Log.Info("\n--- Thread ---");
        Log.Info("CPU Thread Count: \t" + Environment.ProcessorCount);
        Log.Info("Main Thread Id\t" + engine.MainThread);
    }

    public void Dispose()
    {

    }
}
