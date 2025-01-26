using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(400, 400, "Asset Loader HLSL"),
};

using (Game game = new Game(setting))
{
    game.Run();
}

AllocationTracker.CheckAllocated();