using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(800, 450, "Canvas UI"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan,
    }
}.
With<PluginHDR>().
With<PluginDebugGUI>();

//setting.Window.VSync = true;

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();