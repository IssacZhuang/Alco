using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Sprite Rendering"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan,
    }
}.With<PluginDefaultAssets>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();