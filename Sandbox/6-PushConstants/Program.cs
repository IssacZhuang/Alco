using Vocore;
using Vocore.Engine;

GameEngineSetting setting = GameEngineSetting.Default with
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Push Constants"),
};

using (Game game = new Game(setting))
{
    game.Run();
}