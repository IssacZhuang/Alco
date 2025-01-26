using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Basic Window"),
};

using (Game game = new Game(setting))
{
    game.Run();
}