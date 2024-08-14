using Vocore.Graphics;
using SDL3;

using static SDL3.SDL3;
using System.Runtime.CompilerServices;

namespace Vocore.Engine;

public class Sdl3Platform : Platform
{

    
    private readonly Dictionary<SDL_WindowID, Sdl3Window> _windows = new();
    private readonly Sdl3InputSystem _input = new();
    private EngineTimer _timer;
    private bool _isStopped = false;

    public Sdl3Platform()
    {
        _timer = new EngineTimer();
    }

    public override InputSystem Input
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input;
    }

    public override Window CreateWindow(GPUDevice device, WindowSetting setting)
    {
        Sdl3Window window = new Sdl3Window(device, setting);
        _windows.Add(window.WindowId, window);
        return window;
    }

    public override void CloseWindow(Window window)
    {
        if (window is Sdl3Window sdl3Window)
        {
            sdl3Window.Dispose();
            _windows.Remove(sdl3Window.WindowId);
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
            while (SDL_PollEvent(out SDL_Event e))
            {
                HandleEvent(e);
            }

            if (canInvokePhysicsTick)
            {
                DoTick(physicsDeltaTime);
            }

            //Log.Info(updateDeltaTime, physicsDeltaTime);
            DoUpdate(updateDeltaTime);

            _input.Reset();
        }
    }

    public override void StopMainLoop()
    {
        _isStopped = true;
    }

    protected override void Dispose(bool disposing)
    {

    }

    private void HandleEvent(SDL_Event e)
    {
        switch (e.type)
        {

            case SDL_EventType.KeyDown:
                _input.OnSdlKeyDown(e.key.key);
                break;
            case SDL_EventType.KeyUp:
                _input.OnSdlKeyUp(e.key.key);
                break;
            case SDL_EventType.MouseButtonDown:
                _input.OnSdlMouseButtonDown(e.button.button);
                break;
            case SDL_EventType.MouseButtonUp:
                _input.OnSdlMouseButtonUp(e.button.button);
                break;
            case SDL_EventType.Quit:
                StopMainLoop();
                break;
            case SDL_EventType.WindowCloseRequested:
                Log.Info("Window close requested");
                //todo: implement
                break;
            case SDL_EventType.WindowResized:
                Sdl3Window window = _windows[e.window.windowID];
                window.DoResize(new uint2(e.window.data1, e.window.data2));
                break;
        }
    }

    private void ProcessResize()
    {

    }
}