namespace Alco.GUI;

/// <summary>
/// Controls which axis of a UIText node auto-adjusts to match its text content size.
/// </summary>
public enum FitContentMode
{
    /// <summary>
    /// No auto-sizing. Size must be set externally.
    /// </summary>
    None,

    /// <summary>
    /// Auto-fit width to the text content. Size.X is set to the pixel width of the widest line.
    /// Not compatible with OverflowHorizontal.NextLine (ignored if both are set).
    /// </summary>
    Width,

    /// <summary>
    /// Auto-fit height to the text content. Size.Y is set to the content height based on line count and line spacing.
    /// Requires OverflowHorizontal.NextLine for multi-line wrapping.
    /// </summary>
    Height
}
