using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine;

public abstract class Platform : AutoDisposable
{
    /// <summary>
    /// The physics tick event, called every fixed time
    /// </summary>
    public event Action<float>? OnTick;
    /// <summary>
    /// The update event, called every frame
    /// </summary>
    public event Action<float>? OnUpdate;


    /// <summary>
    /// The input handler
    /// </summary>
    /// <value></value>
    public abstract InputSystem Input { get; }

    /// <summary>
    /// Run the main loop of the engine
    /// </summary>
    /// <param name="runOnce">Run the engine once, then stop</param>
    public abstract void RunMainLoop(bool runOnce);

    /// <summary>
    /// Stop the main loop of the engine
    /// </summary>
    public abstract void StopMainLoop();

    /// <summary>
    /// Create a window
    /// </summary>
    /// <param name="device">The graphics device</param>
    /// <param name="setting">The window setting</param>
    /// <returns></returns>
    public abstract Window CreateWindow(GPUDevice device, WindowSetting setting);

    /// <summary>
    /// Close a window
    /// </summary> 
    public abstract void CloseWindow(Window window);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void DoTick(float deltaTime)
    {
        OnTick?.Invoke(deltaTime);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void DoUpdate(float deltaTime)
    {
        OnUpdate?.Invoke(deltaTime);
    }
}