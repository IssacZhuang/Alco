
using System;
using Alco.Engine;
using Alco.Graphics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering.Composition;
using View = Alco.Engine.View;

namespace Alco.Editor;

public class EditorPlatform : Platform
{
    private static readonly InputSystem NoInputSystem = new NoInputSystem();
    private Avalonia.Controls.Window? _avaloniaWindow;
    private Compositor? _compositor;
    private EditorInputSystem? _input;
    private EngineTimer _engineTimer;
    private bool _isRunning = false;

    public EditorPlatform()
    {
        _engineTimer = new EngineTimer();
    }

    public void SetWindow(Avalonia.Controls.Window window)
    {
        _avaloniaWindow = window;
        _compositor = (ElementComposition.GetElementVisual(_avaloniaWindow)?.Compositor) ?? throw new Exception("Compositor is null");
        _input = new EditorInputSystem(_avaloniaWindow);
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

        _input?.Update();
    }

    private void RequestUpdate()
    {
        _compositor?.RequestCompositionUpdate(Update);
    }

    public override InputSystem Input => _input ?? NoInputSystem;

    public override void CloseView(View window)
    {

    }

    public override View CreateView(GPUDevice device, ViewSetting setting)
    {
        return new NoView();
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
        _input?.Dispose();
    }
}

