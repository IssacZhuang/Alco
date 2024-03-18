using Vocore;
using Vocore.Engine;

GameEngineSetting setting = GameEngineSetting.Default with
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Transform2D"),
};

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}