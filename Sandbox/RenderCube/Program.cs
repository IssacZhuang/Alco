using Vocore;
using Vocore.Engine;
using Veldrid;

GameEngineSetting setting = GameEngineSetting.Default;
setting.windowName = "BasicSDL";
setting.backend = GraphicsBackend.Direct3D11;

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}