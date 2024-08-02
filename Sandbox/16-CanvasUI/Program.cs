using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(800, 450, "Canvas UI"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan,
        DebugInfo = true
    }
}.
With<PluginHDR>().
With<PluginDebugGUI>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();