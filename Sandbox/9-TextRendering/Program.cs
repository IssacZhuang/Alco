using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Text Rendering"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan,
    }
}.With<PluginDefaultAssets>().
With<PluginHDR>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();