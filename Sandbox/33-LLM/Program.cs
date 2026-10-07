using _33_LLM;
using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting(1000, 700, "LLM System"),
    Graphics = GraphicsSetting.Default with
    {
        Backend = GraphicsBackend.WGPUVulkan
    },
};

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();

