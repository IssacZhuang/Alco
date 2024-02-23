using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = GameEngineSetting.Default with{
    StopWhenError = true,
    GraphicsAPI = GraphicsBackend.Vulkan,
};
setting.WindowName = "Basic Window";

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}