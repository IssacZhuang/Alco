using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    // Unlimited render rate: the simulation runs on the engine's fixed 60 Hz
    // tick (Game.OnTick) while rendering (Game.OnUpdate) runs as fast as the
    // presentation allows, interpolating the tick-driven bodies between ticks.
    TargetFrameRate = 0,
    View = new ViewSetting(1280, 720, "Smoke Trail Arena"),
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
