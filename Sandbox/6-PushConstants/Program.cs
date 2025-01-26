using Alco;
using Alco.Engine;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Push Constants"),
}.With<PluginDefaultAssets>().With<PluginDebugGUI>();

using (Game game = new Game(setting))
{
    game.Run();
}