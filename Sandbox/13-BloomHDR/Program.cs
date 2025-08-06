using Alco;
using Alco.Engine;
using Alco.Graphics;
using Alco.ImGUI;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting(960, 540, "Bloom HDR"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan
    },
}.
With<PluginHDR>().
With<PluginBloom>().
With<PluginDebugStats>().
With<PluginImGUI>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();