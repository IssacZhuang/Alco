using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(1280, 720, "Bloom HDR"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan,
        SwapChainDepthFormat = null
    },
}.
With<PluginDefaultAssets>().
With<PluginHDR>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();