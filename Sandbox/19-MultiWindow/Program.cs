using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(720, 405, "Multi window"),

    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan,
    }
}.With<PluginDefaultAssets>().
//With<PluginBloom>().
With<PluginDebugGUI>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();