using System.Numerics;
using Alco.Graphics;
using System.Threading.Tasks;

namespace Alco.Engine;

/// <summary>
/// Empty implementation of a window. Usually used for testing and server environments.
/// </summary>
public sealed class NoView : View
{

    /// <summary>
    /// Creates a headless view sized from the given <paramref name="setting"/>.
    /// The size drives the offscreen render texture resolution (and thus the screenshot resolution);
    /// a zero size would collapse the render target to 1×1.
    /// </summary>
    public NoView(ViewSetting setting)
    {
        Size = new uint2(setting.Width, setting.Height);
    }

    /// <inheritdoc />
    public override WindowMode WindowMode { get; set; }

    /// <inheritdoc />
    public override uint2 Size { get; set; }

    /// <inheritdoc />
    public override string Title { get; set; } = "No View";

    public override GPUSwapchain? Swapchain => null;

    public override int2 Position { get; set; }

    public override Vector2 MousePosition
    {
        get => Vector2.Zero;
    }

    protected override void Dispose(bool disposing)
    {
        
    }

    public override void SetTextInputArea(int x, int y, int width, int height, int cursor)
    {

    }

    protected override void StartTextInput()
    {

    }

    protected override void EndTextInput()
    {

    }

    public override void Close()
    {
        
    }

    public override Task<string[]> OpenFilePickerAsync(string? defaultPath, bool allowMultiple, params ReadOnlySpan<DialogFileFilter> filters)
    {
        return Task.FromResult(Array.Empty<string>());
    }

    public override Task<string[]> OpenFolderPickerAsync(string? defaultPath, bool allowMultiple)
    {
        return Task.FromResult(Array.Empty<string>());
    }

    public override Task<string[]> OpenSaveFilePickerAsync(string? defaultPath, params ReadOnlySpan<DialogFileFilter> filters)
    {
        return Task.FromResult(Array.Empty<string>());
    }

    
}
