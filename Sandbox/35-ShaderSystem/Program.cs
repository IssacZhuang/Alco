using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting(400, 400, "ShaderSystem (slang modules)"),
};

using (Game game = new Game(setting))
{
    game.Run();
}

AllocationTracker.CheckAllocated();
