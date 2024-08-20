using Vocore.Graphics;

namespace Vocore.Engine;

/// <summary>
/// Empty implementation of a window. Usually used for testing and server environments.
/// </summary>
public sealed class NoWindow : Window
{

    /// <inheritdoc />
    public override WindowMode WindowMode { get; set; }

    /// <inheritdoc />
    public override uint2 Size { get; set; }

    /// <inheritdoc />
    public override string Title { get; set; } = "No Window";

    public override GPUSwapchain? Swapchain => null;

    public override int2 Position { get; set; }

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
