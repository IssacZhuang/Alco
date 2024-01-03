using Vocore;
using Vocore.Engine;
using Veldrid;

GameEngineSetting setting = GameEngineSetting.Default;
setting.WindowName = "Render Cube";
setting.GraphicsAPI = GraphicsBackend.OpenGLES;
setting.StopWhenError = true;

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.RegisterPlugin<PluginForceMouseInScreenCenter>();
    game.Run();
}