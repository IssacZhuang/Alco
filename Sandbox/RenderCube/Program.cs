using Vocore;
using Vocore.Engine;
using Veldrid;

GameEngineSetting setting = GameEngineSetting.Default;
setting.windowName = "Render Cube";
setting.backend = GraphicsBackend.OpenGL;
setting.stopWhenError = true;

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.RegisterPlugin<PluginForceMouseInScreenCenter>();
    game.Run();
}