using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Alco.Graphics;

namespace Alco.Engine;


/// <summary>
/// The view used for GPU rendering. 
/// It could be a window in standalone application or a view embedded in a UI framework.
/// </summary>
public unsafe abstract class View : AutoDisposable
{
    private readonly WeakEvent<ReadOnlySpan<char>> _onTextInput = new();
    private readonly WeakEvent<uint2> _onResize = new();
    private readonly WeakEvent _onMinimize = new();
    private readonly WeakEvent _onRestore = new();

    private bool _isInputing = false;

    public uint Width => Size.X;
    public uint Height => Size.Y;

    public bool IsTextInputEnabled
    {
        get => _isInputing;
        set
        {
            if (value == _isInputing)
            {
                return;
            }

            if (value)
            {
                StartTextInput();
            }
            else
            {
                EndTextInput();
            }

            _isInputing = value;
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
    public event Action<uint2> OnResize
    {
        add => _onResize.AddListener(value);
        remove => _onResize.RemoveListener(value);
    }

    /// <summary>
    /// The view minimize event. It will destroy the surface texture on this event.
    /// </summary>
    public event Action OnMinimize
    {
        add => _onMinimize.AddListener(value);
        remove => _onMinimize.RemoveListener(value);
    }

    /// <summary>
    /// The view restore event. It will recreate the surface texture on this event.
    /// </summary>
    public event Action OnRestore
    {
        add => _onRestore.AddListener(value);
        remove => _onRestore.RemoveListener(value);
    }

    /// <summary>
    /// The text input event.
    /// </summary>
    public event Action<ReadOnlySpan<char>> OnTextInput
    {
        add => _onTextInput.AddListener(value);
        remove => _onTextInput.RemoveListener(value);
    }

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

    /// <summary>
    /// Opens a file picker dialog.
    /// </summary>
    /// <param name="defaultPath">The initial directory path, or null to use the current directory.</param>
    /// <param name="allowMultiple">Whether multiple selections are allowed.</param>
    /// <param name="filters">The file type filters to apply.</param>
    /// <returns>A task that completes with selected file paths. Empty if canceled.</returns>
    public abstract Task<string[]> OpenFilePickerAsync(string? defaultPath, bool allowMultiple, params ReadOnlySpan<DialogFileFilter> filters);

    /// <summary>
    /// Opens a folder picker dialog.
    /// </summary>
    /// <param name="defaultPath">The initial directory path, or null to use the current directory.</param>
    /// <param name="allowMultiple">Whether multiple selections are allowed.</param>
    /// <returns>A task that completes with selected folder paths. Empty if canceled.</returns>
    public abstract Task<string[]> OpenFolderPickerAsync(string? defaultPath, bool allowMultiple);

    /// <summary>
    /// Opens a save file dialog.
    /// </summary>
    /// <param name="defaultPath">The initial directory path, or null to use the current directory.</param>
    /// <param name="filters">The file type filters to apply.</param>
    /// <returns>A task that completes with the selected save path. Empty if canceled.</returns>
    public abstract Task<string[]> OpenSaveFilePickerAsync(string? defaultPath, params ReadOnlySpan<DialogFileFilter> filters);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DoResize(uint2 size)
    {
        _onResize.Invoke(size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DoMinimize()
    {
        _onMinimize.Invoke();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DoRestore()
    {
        _onRestore.Invoke();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void DoTextInput(ReadOnlySpan<char> text)
    {
        _onTextInput.Invoke(text);
    }
}