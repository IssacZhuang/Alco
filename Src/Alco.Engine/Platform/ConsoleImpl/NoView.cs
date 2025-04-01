using Alco.Graphics;

namespace Alco.Engine;

/// <summary>
/// Empty implementation of a window. Usually used for testing and server environments.
/// </summary>
public sealed class NoView : View
{

    /// <inheritdoc />
    public override WindowMode WindowMode { get; set; }

    /// <inheritdoc />
    public override uint2 Size { get; set; }

    /// <inheritdoc />
    public override string Title { get; set; } = "No View";

    public override GPUSwapchain? Swapchain => null;

    public override int2 Position { get; set; }

    public override int2 MousePosition
    {
        get => new int2(0, 0);
    }

    protected override void Dispose(bool disposing)
    {
        
    }

    public override void StartTextInput(int x, int y, int width, int height, int cursor)
    {

    }

    public override void EndTextInput()
    {

    }

    public override void Close()
    {
        
    }

    
}
