using Veldrid;
using Vocore;
using Vocore.Engine;

GameEngineSetting setting = GameEngineSetting.Default;
setting.backend = GraphicsBackend.OpenGL;
setting.windowName = "Basic Window";

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}