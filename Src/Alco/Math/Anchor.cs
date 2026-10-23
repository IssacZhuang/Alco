using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// A normalized rectangular anchor range for UI layout, with coordinates relative to the parent's center.
/// </summary>
public struct Anchor
{
    public Vector2 min;
    public Vector2 max;

    public Vector2 CenterPoint
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => min + (max - min) * 0.5f;
    }

    /// <summary>
    /// An anchor at the parent's center.
    /// </summary>
    public static readonly Anchor Center = new(new Vector2(0f, 0f), new Vector2(0f, 0f));
    /// <summary>
    /// An anchor at the parent's left edge, vertically centered.
    /// </summary>
    public static readonly Anchor LeftCenter = new(new Vector2(-0.5f, 0f), new Vector2(-0.5f, 0f));
    /// <summary>
    /// An anchor at the parent's right edge, vertically centered.
    /// </summary>
    public static readonly Anchor RightCenter = new(new Vector2(0.5f, 0f), new Vector2(0.5f, 0f));
    /// <summary>
    /// An anchor at the parent's top edge, horizontally centered.
    /// </summary>
    public static readonly Anchor CenterTop = new(new Vector2(0f, 0.5f), new Vector2(0f, 0.5f));
    /// <summary>
    /// An anchor at the parent's bottom edge, horizontally centered.
    /// </summary>
    public static readonly Anchor CenterBottom = new(new Vector2(0f, -0.5f), new Vector2(0f, -0.5f));

    /// <summary>
    /// An anchor at the parent's top-left corner.
    /// </summary>
    public static readonly Anchor LeftTop = new(new Vector2(-0.5f, 0.5f), new Vector2(-0.5f, 0.5f));
    /// <summary>
    /// An anchor at the parent's top-right corner.
    /// </summary>
    public static readonly Anchor RightTop = new(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));
    /// <summary>
    /// An anchor at the parent's bottom-left corner.
    /// </summary>
    public static readonly Anchor LeftBottom = new(new Vector2(-0.5f, -0.5f), new Vector2(-0.5f, -0.5f));
    /// <summary>
    /// An anchor at the parent's bottom-right corner.
    /// </summary>
    public static readonly Anchor RightBottom = new(new Vector2(0.5f, -0.5f), new Vector2(0.5f, -0.5f));

    /// <summary>
    /// An anchor that fills the parent on both axes.
    /// </summary>
    public static readonly Anchor Stretch = new(new Vector2(-0.5f, -0.5f), new Vector2(0.5f, 0.5f));
    /// <summary>
    /// An anchor on the parent's left edge, stretched vertically.
    /// </summary>
    public static readonly Anchor LeftVerticalStretch = new(new Vector2(-0.5f, -0.5f), new Vector2(-0.5f, 0.5f));
    /// <summary>
    /// An anchor centered horizontally and stretched vertically.
    /// </summary>
    public static readonly Anchor CenterVerticalStretch = new(new Vector2(0f, -0.5f), new Vector2(0f, 0.5f));
    /// <summary>
    /// An anchor on the parent's right edge, stretched vertically.
    /// </summary>
    public static readonly Anchor RightVerticalStretch = new(new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f));

    /// <summary>
    /// An anchor on the parent's top edge, stretched horizontally.
    /// </summary>
    public static readonly Anchor TopHorizontalStretch = new(new Vector2(-0.5f, 0.5f), new Vector2(0.5f, 0.5f));
    /// <summary>
    /// An anchor centered vertically and stretched horizontally.
    /// </summary>
    public static readonly Anchor CenterHorizontalStretch = new(new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f));
    /// <summary>
    /// An anchor on the parent's bottom edge, stretched horizontally.
    /// </summary>
    public static readonly Anchor BottomHorizontalStretch = new(new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f));

    /// <summary>
    /// Creates an anchor from its minimum and maximum normalized positions.
    /// </summary>
    /// <param name="min">The minimum normalized position.</param>
    /// <param name="max">The maximum normalized position.</param>
    public Anchor(Vector2 min, Vector2 max)
    {
        this.min = min;
        this.max = max;
    }

    /// <summary>
    /// Removes vertical stretch from an anchor, collapsing its Y range to the center.
    /// </summary>
    /// <param name="anchor">The anchor to destretch.</param>
    /// <returns>The anchor without vertical stretch.</returns>
    public static Anchor DestretchVertical(Anchor anchor)
    {
        return new Anchor(new Vector2(anchor.min.X, 0f), new Vector2(anchor.max.X, 0f));
    }

    /// <summary>
    /// Removes horizontal stretch from an anchor, collapsing its X range to the center.
    /// </summary>
    /// <param name="anchor">The anchor to destretch.</param>
    /// <returns>The anchor without horizontal stretch.</returns>
    public static Anchor DestretchHorizontal(Anchor anchor)
    {
        return new Anchor(new Vector2(0f, anchor.min.Y), new Vector2(0f, anchor.max.Y));
    }
}