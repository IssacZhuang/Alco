using Vocore;
using Vocore.Engine;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(400, 400, "Compute Shader"),
};

using (Game game = new Game(setting))
{
    game.Run();
}
