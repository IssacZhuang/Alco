using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = GameEngineSetting.Default with
{
    StopWhenError = true,
    Window = new WindowSetting(400, 400, "Asset Loader HLSL"),
};

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}

AllocationTracker.CheckAllocated();