using Vocore;
using Vocore.Engine;

using (Game game = new Game())
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}