using Vocore.Graphics;
using SDL3;

using static SDL3.SDL3;

namespace Vocore.Engine;

public class Sdl3Platform : Platform
{
    private readonly EngineTimer _timer = new();
    private readonly List<Sdl3Window> _windows = new();
    private bool _isStopped = false;


    public override Window CreateWindow(GPUDevice device, WindowSetting setting)
    {
        Sdl3Window window = new Sdl3Window(device, setting);
        _windows.Add(window);
        return window;
    }

    public override void CloseWindow(Window window)
    {
        if (window is Sdl3Window sdl3Window)
        {
            sdl3Window.Dispose();
            _windows.Remove(sdl3Window);
            return;
        }

        throw new InvalidOperationException("Invalid window type");
    }

    public override void RunMainLoop()
    {
        _timer.Start();
        while (!_isStopped)
        {
            _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);
            
            if (canInvokePhysicsTick)
            {
                DoTick(physicsDeltaTime);
            }

            DoUpdate(updateDeltaTime);
        }
    }

    public override void StopMainLoop()
    {
        _isStopped = true;
    }

    protected override void Dispose(bool disposing)
    {

    }
}