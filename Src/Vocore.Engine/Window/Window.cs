namespace Vocore.Engine;

/// <summary>
/// Represents a window in the application.
/// </summary>
public abstract class Window
{
    /// <summary>
    /// Gets or sets the window mode.
    /// </summary>
    public abstract WindowMode WindowMode { get; set; }

    /// <summary>
    /// Gets or sets the size of the window.
    /// </summary>
    public abstract int2 Size { get; set; }

    /// <summary>
    /// Gets the aspect ratio of the window.
    /// </summary>
    public abstract float AspectRatio { get; }

    /// <summary>
    /// Gets or sets the title of the window.
    /// </summary>
    public abstract string Title { get; set; }

    internal Action<int2>? OnResize;

    internal abstract void Close();
}