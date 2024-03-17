using Vocore;
using Vocore.Engine;
using Vocore.Graphics;



GameEngineSetting setting = GameEngineSetting.Default with
{
    StopWhenError = true,
    GraphicsAPI = GraphicsBackend.None
};
setting.WindowName = "Basic Window";

using (Generator game = new Generator(setting))
{
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}