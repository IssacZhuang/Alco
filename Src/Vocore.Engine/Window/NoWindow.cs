namespace Vocore.Engine;

/// <summary>
/// Empty implementation of a window. Usually used for testing and server environments.
/// </summary>
public sealed class NoWindow : Window
{
    /// <inheritdoc />
    public override WindowMode WindowMode { get; set; }

    /// <inheritdoc />
    public override int2 Size { get; set; }

    /// <inheritdoc />
    public override float AspectRatio { get; }

    /// <inheritdoc />
    public override string Title { get; set; } = "No Window";

    protected override void Dispose(bool disposing)
    {
        
    }

    internal override void Close()
    {
        
    }
}
