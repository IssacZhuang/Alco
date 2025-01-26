using Alco;
using Alco.Engine;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Compute Buffer"),
};


using (Game game = new Game(setting))
{
    game.Run();
}