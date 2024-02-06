using Veldrid;
using Vocore;
using Vocore.Engine;

GameEngineSetting setting = GameEngineSetting.Default with{
    StopWhenError = true,
};
setting.WindowName = "Basic Window";

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}