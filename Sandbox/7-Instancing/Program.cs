using Vocore;
using Vocore.Engine;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Instancing"),
};

using (Game game = new Game(setting))
{
    game.Run();
}