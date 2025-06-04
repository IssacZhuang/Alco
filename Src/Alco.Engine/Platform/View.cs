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
    private bool _isInputing = false;

    public uint Width => Size.X;
    public uint Height => Size.Y;

    public bool IsTextInputEnabled
    {
        get => _isInputing;
        set
        {   
            if(value == _isInputing)
            {
                return;
            }

            if(value)
            {
                StartTextInput();
            }
            else
            {
                EndTextInput();
            }
        }
    }

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
    /// Gets the mouse position in the view. This is the local position of the mouse. Use <see cref="Input.MousePosition"/> for global position.
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

    /// <summary>
    /// Set the text input area in view space.
    /// </summary>
    /// <param name="x">The x coordinate of the top-left corner of the text input area.</param>
    /// <param name="y">The y coordinate of the top-left corner of the text input area.</param>
    /// <param name="width">The width of the text input area.</param>
    /// <param name="height">The height of the text input area.</param>
    /// <param name="cursor">The cursor position of the text input area.</param>
    public abstract void SetTextInputArea(int x, int y, int width, int height, int cursor);

    protected abstract void StartTextInput();

    protected abstract void EndTextInput();

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