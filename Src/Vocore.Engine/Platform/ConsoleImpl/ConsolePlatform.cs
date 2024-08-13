using Vocore.Graphics;

namespace Vocore.Engine;

public class ConsolePlatform : Platform
{
    public override void CloseWindow(Window window)
    {
        
    }

    public override Window CreateWindow(GPUDevice device, WindowSetting setting)
    {
        return new NoWindow();
    }

    public override void RunMainLoop()
    {
        //todo: implement
    }

    public override void StopMainLoop()
    {
        //todo: implement
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}