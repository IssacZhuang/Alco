using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Canvas UI"),
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