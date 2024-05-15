namespace Vocore.GUI;

public enum TextOverflowMode
{
    /// <summary>
    /// Let it overflow.
    /// </summary>
    None,
    /// <summary>
    /// Clamp the text by the text node size.
    /// </summary>
    Clamp,
    /// <summary>
    /// Go to the next line.
    /// </summary>
    NextLine,
}