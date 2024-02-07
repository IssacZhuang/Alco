using Vocore;
using Vocore.Engine;

GameEngineSetting setting = GameEngineSetting.Default with
{
    StopWhenError = true,
    RenderingSetting = RenderingSetting.ForwardNoDepth,
    Width = 400,
    Height = 400
};
setting.WindowName = "Texture Binding";

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}
