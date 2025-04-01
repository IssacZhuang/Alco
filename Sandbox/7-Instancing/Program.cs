using Alco;
using Alco.Engine;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting(640, 360, "Instancing"),
};

using (Game game = new Game(setting))
{
    game.Run();
}