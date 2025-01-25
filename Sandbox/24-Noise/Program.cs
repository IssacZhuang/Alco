using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Noise"),
    Graphics = GraphicsSetting.Default with
    {
        Backend = GraphicsBackend.Vulkan
    },
}.
With<PluginDefaultAssets>().
With(new PluginHDR(1f, 1 / 2.2f)).
// With<PluginBloom>().
With<PluginDebugGUI>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();