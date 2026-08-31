using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting(1280, 720, "PBR Deferred"),
    Graphics = GraphicsSetting.Default with
    {
        Backend = GraphicsBackend.WGPUVulkan
    },
};

using (Game game = new Game(setting, args))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();
