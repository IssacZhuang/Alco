using Alco;
using Alco.Engine;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting(400, 400, "Texture Binding"),
};

using (Game game = new Game(setting))
{
    game.Run();
}
