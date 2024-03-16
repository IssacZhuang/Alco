using System.Numerics;

namespace Vocore.Rendering;

/// <summary>
/// The TextAlign for text rendering.
/// </summary>
public struct TextAlign
{
    //origin point is left-bottom
    public static readonly TextAlign Center = new(-0.5f, -0.5f);
    public static readonly TextAlign LeftCenter = new(-1f, -0.5f);
    public static readonly TextAlign RightCenter = new(0, -0.5f);
    public static readonly TextAlign CenterTop = new(-0.5f, 0);
    public static readonly TextAlign CenterBottom = new(-0.5f, -1f);
    public static readonly TextAlign LeftTop = new(-1f, 0);
    public static readonly TextAlign RightTop = new(0, 0);
    public static readonly TextAlign LeftBottom = new(-1f, -1f);
    public static readonly TextAlign RightBottom = new(0, -1f);

    public TextAlign(float x, float y)
    {
        offsetMultiplier = new Vector2(x, y);
    }

    /// <summary>
    /// text_position = input_position + size * offsetMultiplier
    /// </summary>
    public Vector2 offsetMultiplier;

    public static implicit operator TextAlign(Vector2 v) => new(v.X, v.Y);
}