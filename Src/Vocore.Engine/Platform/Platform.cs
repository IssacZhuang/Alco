using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine;

public abstract class Platform : AutoDisposable
{
    public event Action<float>? OnTick;
    public event Action<float>? OnUpdate;

    public abstract InputSystem Input { get; }

    public abstract void RunMainLoop(bool runOnce);

    public abstract void StopMainLoop();

    public abstract Window CreateWindow(GPUDevice device, WindowSetting setting);
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