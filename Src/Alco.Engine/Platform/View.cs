using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Engine;


/// <summary>
/// The view used for GPU rendering. 
/// It could be a window in standalone application or a view embedded in a UI framework.
/// </summary>
public unsafe abstract class View : AutoDisposable
{

    public uint Width => Size.X;
    public uint Height => Size.Y;

    /// <summary>
    /// Gets or sets the window mode.
    /// Only worked if the view is a window.
    /// </summary>
    public abstract WindowMode WindowMode { get; set; }

    /// <summary>
    /// Gets or sets the position of the view.
    /// Only worked if the view is a window.
    /// </summary> 
    public abstract int2 Position { get; set; }

    /// <summary>
    /// Gets the mouse position in the view. This is the local position of the mouse. Use <see cref="InputSystem.MousePosition"/> for global position.
    /// </summary>
    public abstract Vector2 MousePosition { get; }

    /// <summary>
    /// Gets or sets the size of the view.
    /// </summary>
    public abstract uint2 Size { get; set; }

    /// <summary>
    /// Gets or sets the title of the view.
    /// </summary>
    public abstract string Title { get; set; }

    /// <summary>
    /// The swapchain of the view.
    /// </summary>
    /// <value></value>
    public abstract GPUSwapchain? Swapchain { get; }

    /// <summary>
    /// The view resize event, it can be called anytime. It is unsafe to delete the GPU resources in the event. Use <see cref="ViewRenderTarget.OnResize"/> for safe deletion.
    /// </summary>
    public event Action<uint2>? OnResize;

    /// <summary>
    /// The view minimize event. It will destroy the surface texture on this event.
    /// </summary>
    public event Action? OnMinimize;

    /// <summary>
    /// The view restore event. It will recreate the surface texture on this event.
    /// </summary>
    public event Action? OnRestore;

    /// <summary>
    /// The text input event.
    /// </summary>
    public event Action<string>? OnTextInput;

    /// <summary>
    /// Close the view.
    /// </summary>
    public abstract void Close();


    /// <summary>
    /// Gets the aspect ratio of the view.
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
    protected void DoTextInput(string text)
    {
        OnTextInput?.Invoke(text);
    }
}