namespace Alco.GUI;

/// <summary>
/// The root node of a UI tree, hosting the canvas that renders it.
/// </summary>
public sealed class UIRoot : UINode
{
    /// <summary>Gets the canvas this root belongs to.</summary>
    public Canvas Canvas { get; }

    /// <summary>
    /// Initializes a new root node for the given canvas.
    /// </summary>
    /// <param name="canvas">The canvas this root belongs to.</param>
    public UIRoot(Canvas canvas)
    {
        Canvas = canvas;
    }
}