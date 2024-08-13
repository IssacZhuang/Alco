using Vocore;
using Vocore.Engine;
using Vocore.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    RunOnce = true,
    Graphics = GraphicsSetting.Default,
    Platform = new ConsolePlatform()
}.With<PluginDefaultAssets>();

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();