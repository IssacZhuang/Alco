using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Collision"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan,
        SwapChainDepthFormat = null
    },
}.
With<PluginDefaultAssets>().
With<PluginHDR>().
With<PluginBloom>().
With<PluginDebugGUI>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();