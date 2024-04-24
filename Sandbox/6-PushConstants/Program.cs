using Vocore;
using Vocore.Engine;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Push Constants"),
}.With<PluginDefaultAssets>().With<PluginImGui>();

using (Game game = new Game(setting))
{
    game.Run();
}