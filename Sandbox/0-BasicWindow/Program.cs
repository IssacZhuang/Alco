using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = GameEngineSetting.Default with{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Basic Window"),
};

using (Game game = new Game(setting))
{
    game.Run();
}