using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(960, 540, "Space Partition"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan
    },
}.
With<PluginDefaultAssets>().
With<PluginHDR>().
//With<PluginBloom>().
With<PluginDebugGUI>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();