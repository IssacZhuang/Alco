using Alco.Graphics;
using SDL3;

using static SDL3.SDL3;
using System.Runtime.CompilerServices;

namespace Alco.Engine;

public unsafe class Sdl3Platform : Platform
{
    private const int PeepEventsCount = 64;
    private readonly Dictionary<SDL_WindowID, Sdl3Window> _windows = new();
    private readonly Sdl3Input _input = new();
    private NativeBuffer<SDL_Event> _events;
    private EngineTimer _timer;
    private bool _isStopped = false;
    private bool _shouldCapture = false;
    private uint _captureId = 0;

    public Sdl3Platform()
    {
        _timer = new EngineTimer();
        _events = new NativeBuffer<SDL_Event>(PeepEventsCount);
    }

    public override Input Input
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input;
    }

    public override View CreateView(GPUDevice device, ViewSetting setting)
    {
        Sdl3Window window = new Sdl3Window(device, setting);
        _windows.Add(window.WindowId, window);
        return window;
    }

    public override void CloseView(View window)
    {
        if (window is Sdl3Window sdl3Window)
        {
            sdl3Window.Dispose();
            _windows.Remove(sdl3Window.WindowId);
            return;
        }

        throw new InvalidOperationException("Invalid window type");
    }

    public override void RunMainLoop(bool runOnce)
    {
        if(runOnce)
        {
            _timer.Start();
            _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);
            DoTick(physicsDeltaTime);
            DoUpdate(updateDeltaTime);
            return;
        }

        _timer.Start();

        _input.Init();
        while (!_isStopped)
        {
            VisualStudioProfiler? profiler = null;
            if (_shouldCapture)
            {
                profiler = new VisualStudioProfiler($"GameEngine.Capture_{_captureId}");
                _captureId++;
                _shouldCapture = false;
            }

            _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);

            if (canInvokePhysicsTick)
            {
                DoTick(physicsDeltaTime);
            }

            SDL_PumpEvents();
            int eventRead;
            do
            {
                eventRead = SDL_PeepEvents(_events.UnsafePointer, PeepEventsCount, SDL_EventAction.GetEvent, SDL_EventType.First, SDL_EventType.Last);
                for (int i = 0; i < eventRead; i++)
                {
                    HandleEvent(_events[i]);
                }
            } while (eventRead > 0);

            DoUpdate(updateDeltaTime);

            _input.Update();

            profiler?.Dispose();
        }
    }

    public override void StopMainLoop()
    {
        _isStopped = true;
    }

    protected override void Dispose(bool disposing)
    {
        if(disposing)
        {
            foreach (var window in _windows.Values)
            {
                window.Dispose();
            }
        }
        _windows.Clear();
        _events.Dispose();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void HandleEvent(SDL_Event e)
    {
        switch (e.type)
        {
            case SDL_EventType.KeyDown:
                if (e.key.key == SDL_Keycode.F12)
                {
                    _shouldCapture = true;
                }
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
            case SDL_EventType.MouseWheel:
                _input.OnSdlMouseWheel(e.wheel.y);
                break;
            case SDL_EventType.TextInput:
                Sdl3Window window1 = _windows[e.window.windowID];
                window1.DoTextInputCore(e.text.GetText() ?? string.Empty);
                break;
            case SDL_EventType.Quit:
                StopMainLoop();
                break;
            case SDL_EventType.WindowResized:
                Sdl3Window window = _windows[e.window.windowID];
                window.DoResize(new uint2(e.window.data1, e.window.data2));
                break;
            case SDL_EventType.WindowMinimized:
                Sdl3Window window2 = _windows[e.window.windowID];
                window2.DoMinimize();
                break;
            case SDL_EventType.WindowRestored:
                Sdl3Window windo3 = _windows[e.window.windowID];
                windo3.DoRestore();
                break;
            case SDL_EventType.WindowFocusGained:
                Sdl3Window window4 = _windows[e.window.windowID];
                window4.IsTextInputEnabled = true;
                break;
            case SDL_EventType.WindowFocusLost:
                Sdl3Window window5 = _windows[e.window.windowID];
                window5.IsTextInputEnabled = false;
                break;
        }
    }
}