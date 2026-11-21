using Alco;
using Alco.Engine;

GameEngineSetting setting = new()
{
    StopWhenError = true,
    View = new ViewSetting(640, 360, "ImGUI"),
    Graphics = GraphicsSetting.Default,
};

using (Game game = new(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();
