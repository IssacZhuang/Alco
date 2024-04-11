using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = GameEngineSetting.Default with
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Text Rendering"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan,
    }
};

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();