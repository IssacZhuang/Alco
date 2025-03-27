
using System;
using Alco.Engine;
using Alco.Graphics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering.Composition;
using Window = Alco.Engine.Window;

namespace Alco.Editor;

public class EditorPlatform : Platform
{
    private Avalonia.Controls.Window? _avaloniaWindow;
    private Compositor? _compositor;
    private InputSystem _input;
    private EngineTimer _engineTimer;
    private bool _isRunning = false;

    public EditorPlatform()
    {
        _engineTimer = new EngineTimer();
        _input = new NoInputSystem();
    }

    public void SetWindow(Avalonia.Controls.Window window)
    {
        _avaloniaWindow = window;
        _compositor = (ElementComposition.GetElementVisual(_avaloniaWindow)?.Compositor) ?? throw new Exception("Compositor is null");
    }

    private void Update()
    {
        _engineTimer.ProcessTime(out float updateDeltaTime, out float physicsDeltaTime, out bool canInvokePhysicsTick);
        if (canInvokePhysicsTick)
        {
            DoTick(physicsDeltaTime);
        }
        DoUpdate(updateDeltaTime);

        if (_isRunning)
        {
            RequestUpdate();
        }
    }

    private void RequestUpdate()
    {
        _compositor?.RequestCompositionUpdate(Update);
    }

    public override InputSystem Input => _input;

    public override void CloseWindow(Window window)
    {
        
    }   

    public override Window CreateWindow(GPUDevice device, WindowSetting setting)
    {
        return new NoWindow();
    }

    public override void RunMainLoop(bool runOnce)
    {
        if(_compositor == null)
        {
            throw new Exception("Compositor is null");
        }
        _engineTimer.Start();
        _isRunning = true;
        RequestUpdate();
    }

    public override void StopMainLoop()
    {
        _isRunning = false;
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}

