using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    // Capped so the screenshot mode's frame-keyed volley and the bullet
    // kinematics run at a deterministic pace (unlimited would bunch the volley).
    TargetFrameRate = 60,
    View = new ViewSetting(1280, 720, "Smoke Trail"),
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
