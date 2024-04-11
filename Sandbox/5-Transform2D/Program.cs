using Vocore;
using Vocore.Engine;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Transform2D"),
};

using (Game game = new Game(setting))
{
    game.Run();
}