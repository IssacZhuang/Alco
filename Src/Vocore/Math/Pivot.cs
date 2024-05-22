using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore;

/// <summary>
/// The alignment relative to the size of the object.
/// </summary>
public struct Pivot
{
    //origin point is left-bottom
    public static readonly Pivot Center = new(0, 0);
    public static readonly Pivot LeftCenter = new(-0.5f, 0);
    public static readonly Pivot RightCenter = new(0.5f, 0);
    public static readonly Pivot CenterTop = new(0, 0.5f);
    public static readonly Pivot CenterBottom = new(0, -0.5f);
    public static readonly Pivot LeftTop = new(-0.5f, 0.5f);
    public static readonly Pivot LeftBottom = new(-0.5f, -0.5f);
    public static readonly Pivot RightTop = new(0.5f, 0.5f);
    public static readonly Pivot RightBottom = new(0.5f, -0.5f);

    public float X
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => value.X;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.value.X = value;
    }

    public float Y
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => value.Y;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.value.Y = value;
    }

    public Pivot(float x, float y)
    {
        value = new Vector2(x, y);
    }

    /// <summary>
    /// text_position = input_position + size * offsetMultiplier
    /// </summary>
    public Vector2 value;

    public static implicit operator Pivot(Vector2 v) => new(v.X, v.Y);
    public static implicit operator Vector2(Pivot p) => p.value;
}