using Alco.Graphics;
using SDL3;

using static SDL3.SDL3;
using System.Runtime.CompilerServices;
using System.Text;
using System.Runtime.InteropServices;

namespace Alco.Engine;

/// <summary>
/// Hosts the engine main loop, input, and window integration on SDL 3.
/// </summary>
public unsafe class Sdl3Platform : Platform
{
    public const int StackAllocationCharSizeLimit = 1024;

    private const int PeepEventsCount = 64;
    private readonly Dictionary<SDL_WindowID, Sdl3Window> _windows = new();
    private readonly Sdl3Input _input = new();
    private NativeBuffer<SDL_Event> _events;
    private EngineTimer _timer;
    private bool _isStopped = false;
    private bool _shouldCapture = false;
    private bool _isDrainingInitialEvents = false;
    private uint _captureId = 0;

    /// <summary>
    /// Initializes a new SDL 3 platform.
    /// </summary>
    public Sdl3Platform()
    {
        _timer = new EngineTimer();
        _events = new NativeBuffer<SDL_Event>(PeepEventsCount);
    }

    /// <inheritdoc/>
    public override Input Input
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input;
    }

    /// <inheritdoc/>
    public override int TargetFrameRate
    {
        get => base.TargetFrameRate;
        set
        {
            base.TargetFrameRate = value;
            _timer.SetTargetFrameRate(value);
        }
    }

    /// <inheritdoc/>
    public override View CreateView(GPUDevice device, ViewSetting setting)
    {
        Sdl3Window window = new Sdl3Window(device, setting);
        _windows.Add(window.WindowId, window);
        // The first created view is the main window: bind it as the target for
        // relative (raw) mouse input.
        _input.RelativeMouseWindow ??= window;
        return window;
    }

    /// <inheritdoc/>
    public override void CloseView(View window)
    {
        if (window is Sdl3Window sdl3Window)
        {
            _input.ReleaseRelativeMouseWindow(sdl3Window);
            sdl3Window.Dispose();
            _windows.Remove(sdl3Window.WindowId);
            return;
        }

        throw new InvalidOperationException("Invalid window type");
    }

    /// <inheritdoc/>
    public override void RunMainLoop(bool runOnce)
    {
        // Initialize subsystems.
        SDL_Init(SDL_InitFlags.Audio | SDL_InitFlags.Joystick | SDL_InitFlags.Gamepad);

        if (runOnce)
        {
            _timer.Start();
            _timer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);
            DoTick(physicsDeltaTime);
            DoUpdate(updateDeltaTime);
            return;
        }

        _input.Init();
        _timer.Start(TargetFrameRate);
        _isDrainingInitialEvents = true;
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
            _isDrainingInitialEvents = false;

            DoUpdate(updateDeltaTime);

            _input.Update();

            profiler?.Dispose();

            _timer.WaitForNextFrame();
        }
    }

    /// <inheritdoc/>
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
                _input.OnSdlMouseWheel(e.wheel.x, e.wheel.y);
                break;
            case SDL_EventType.TextInput:
                Sdl3Window window1 = _windows[e.window.windowID];
                ReadOnlySpan<byte> str = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(e.text.text);
                int length = Encoding.UTF8.GetCharCount(str);
                if (length <= StackAllocationCharSizeLimit)
                {
                    Span<char> chars = stackalloc char[length];
                    Encoding.UTF8.GetChars(str, chars);
                    window1.DoTextInputCore(chars);
                }
                else
                {
                    window1.DoTextInputCore(e.text.GetText() ?? string.Empty);
                }


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
                Sdl3Window window3 = _windows[e.window.windowID];
                window3.DoRestore();
                break;
            case SDL_EventType.WindowFocusGained:
                Sdl3Window window4 = _windows[e.window.windowID];
                window4.ResumeTextInputIfNeeded();
                break;
            case SDL_EventType.WindowFocusLost:
                Sdl3Window window5 = _windows[e.window.windowID];
                window5.ForceStopTextInput();
                break;
            case SDL_EventType.GamepadButtonDown:
                _input.OnSdlGamepadButtonDown(e.gbutton.which, (SDL_GamepadButton)e.gbutton.button);
                break;
            case SDL_EventType.GamepadButtonUp:
                _input.OnSdlGamepadButtonUp(e.gbutton.which, (SDL_GamepadButton)e.gbutton.button);
                break;
            case SDL_EventType.GamepadAxisMotion:
                _input.OnSdlGamepadAxisMotion(e.gaxis.which, (SDL_GamepadAxis)e.gaxis.axis, e.gaxis.value);
                break;
            case SDL_EventType.AudioDeviceAdded:
                if (!e.adevice.recording && !_isDrainingInitialEvents)
                    DoAudioDefaultDeviceChanged();
                break;
            case SDL_EventType.AudioDeviceRemoved:
                if (!e.adevice.recording)
                    DoAudioDefaultDeviceChanged();
                break;
        }
    }
}
