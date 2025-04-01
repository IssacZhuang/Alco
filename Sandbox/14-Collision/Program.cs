using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting(640, 360, "Collision"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan
    },
}.
With<PluginDefaultAssets>().
With<PluginHDR>().
With<PluginBloom>().
With<PluginDebugGUI>().
With<PluginImGUI>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();