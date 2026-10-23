namespace Alco.GUI;

/// <summary>
/// How text that exceeds the node width is laid out.
/// </summary>
public enum OverflowModeHorizontal
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

/// <summary>
/// How text that exceeds the node height is handled.
/// </summary>
public enum OverflowModeVertical
{
    /// <summary>
    /// Let it overflow.
    /// </summary>
    None,
    /// <summary>
    /// Clamp the text by the text node size.
    /// </summary>
    Clamp,
}