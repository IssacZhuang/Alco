using Vocore.Graphics;

namespace Vocore.Engine;

/// <summary>
/// Empty implementation of a window. Usually used for testing and server environments.
/// </summary>
public sealed class NoWindow : Window
{
    public static readonly NoInputSystem NoInputSystem = new NoInputSystem();

    /// <inheritdoc />
    public override WindowMode WindowMode { get; set; }

    /// <inheritdoc />
    public override int2 Size { get; set; }

    /// <inheritdoc />
    public override string Title { get; set; } = "No Window";

    public override GPUSwapchain? Swapchain => null;

    public override InputSystem Input => NoInputSystem;

    public override int2 Position { get; set; }

    protected override void Dispose(bool disposing)
    {
        
    }

    public override void Close()
    {
        
    }
}
