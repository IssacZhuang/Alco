using System.Numerics;

namespace Vocore.Rendering;

/// <summary>
/// The TextAlign for text rendering.
/// </summary>
public struct Anchor
{
    //origin point is left-bottom
    public static readonly Anchor Center = new(0.5f, 0.5f);
    public static readonly Anchor LeftCenter = new(0f, 0.5f);
    public static readonly Anchor RightCenter = new(1f, 0.5f);
    public static readonly Anchor CenterTop = new(0.5f, 1f);
    public static readonly Anchor CenterBottom = new(0.5f, 0f);
    public static readonly Anchor LeftTop = new(0f, 1f);
    public static readonly Anchor RightTop = new(1f, 1f);
    public static readonly Anchor LeftBottom = new(0f, 0f);
    public static readonly Anchor RightBottom = new(1f, 0f);

    public Anchor(float x, float y)
    {
        value = new Vector2(x, y);
    }

    /// <summary>
    /// text_position = input_position + size * offsetMultiplier
    /// </summary>
    public Vector2 value;

    public static implicit operator Anchor(Vector2 v) => new(v.X, v.Y);
}