using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting(1280, 720, "SVG Rendering"),
    Graphics = GraphicsSetting.Default with
    {
        Backend = GraphicsBackend.Vulkan,
    }
};

using (Game game = new Game(setting, args))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();
return 0;
