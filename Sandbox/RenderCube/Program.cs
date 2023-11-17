using Vocore;
using Vocore.Engine;

GameEngineSetting setting = GameEngineSetting.Default;
setting.windowName = "BasicSDL";

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}