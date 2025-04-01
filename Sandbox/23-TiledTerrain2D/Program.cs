using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting(640, 360, "Tiled Terrain 2D"),
    Graphics = GraphicsSetting.Default with
    {
        Backend = GraphicsBackend.Vulkan
    },
}.
With<PluginDefaultAssets>().
With(new PluginHDR()).
//With<PluginBloom>().
With<PluginDebugGUI>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();