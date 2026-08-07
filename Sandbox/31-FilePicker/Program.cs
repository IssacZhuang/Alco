using Alco;
using Alco.Engine;
using Alco.Graphics;

GameEngineSetting setting = new GameEngineSetting
{
    StopWhenError = true,
    View = new ViewSetting(800, 480, "File Picker (ImGUI)"),
    Graphics = GraphicsSetting.Default with
    {
        Backend = GraphicsBackend.Vulkan
    },
};

using (Game game = new Game(setting))
{
    game.Run();
}

GC.Collect();
GC.WaitForFullGCComplete();
AllocationTracker.CheckAllocated();


