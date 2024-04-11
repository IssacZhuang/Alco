using Vocore;
using Vocore.Engine;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Draw Quad"),
};

using (Game game = new Game(setting))
{
    game.Run();
}