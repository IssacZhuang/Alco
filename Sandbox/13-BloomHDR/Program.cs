using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = GameEngineSetting.Default with
{
    StopWhenError = true,
    Window = new WindowSetting(640, 360, "Bloom HDR"),
    Graphics = GraphicsSetting.Default with{
        Backend = GraphicsBackend.Vulkan,
        VSync = true
    }
};

using (Game game = new Game(setting))
{
    game.RegisterPlugin<PluginBuiltInShader>();
    game.RegisterPlugin<PluginRuntimeInfo>();
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();