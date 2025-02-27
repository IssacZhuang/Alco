using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Engine;


/// <summary>
/// Represents a window in the application.
/// </summary>
public unsafe abstract class Window : AutoDisposable//todo : change to disposable
{

    public uint Width => Size.X;
    public uint Height => Size.Y;

    /// <summary>
    /// Gets or sets the window mode.
    /// </summary>
    public abstract WindowMode WindowMode { get; set; }

    /// <summary>
    /// Gets or sets the position of the window.
    /// </summary> 
    public abstract int2 Position { get; set; }

    /// <summary>
    /// Gets the mouse position in the window. This is the local position of the mouse. Use <see cref="InputSystem.MousePosition"/> for global position.
    /// </summary>
    public abstract int2 MousePosition { get; }

    /// <summary>
    /// Gets or sets the size of the window.
    /// </summary>
    public abstract uint2 Size { get; set; }

    /// <summary>
    /// Gets or sets the title of the window.
    /// </summary>
    public abstract string Title { get; set; }

    /// <summary>
    /// The swapchain of the window.
    /// </summary>
    /// <value></value>
    public abstract GPUSwapchain? Swapchain { get; }

    /// <summary>
    /// The window resize event, it can be called anytime. It is unsafe to delete the GPU resources in the event. Use <see cref="WindowRenderTarget.OnResize"/> for safe deletion.
    /// </summary>
    public event Action<uint2>? OnResize;

    /// <summary>
    /// The window minimize event. It will destroy the surface texture on this event.
    /// </summary>
    public event Action? OnMinimize;

    /// <summary>
    /// The window restore event. It will recreate the surface texture on this event.
    /// </summary>
    public event Action? OnRestore;

    /// <summary>
    /// The text input event.
    /// </summary>
    public event Action<string>? OnTextInput;

    /// <summary>
    /// Close the window.
    /// </summary>
    public abstract void Close();


    /// <summary>
    /// Gets the aspect ratio of the window.
    /// </summary>
    public float AspectRatio
    {
        get => (float)Size.X / Size.Y;
    }

    public abstract void StartTextInput(int x, int y, int width, int height, int cursor);

    public abstract void EndTextInput();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DoResize(uint2 size)
    {
        OnResize?.Invoke(size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DoMinimize()
    {
        OnMinimize?.Invoke();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DoRestore()
    {
        OnRestore?.Invoke();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DoTextInput(string text)
    {
        OnTextInput?.Invoke(text);
    }
}