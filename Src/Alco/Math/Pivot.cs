using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco;

/// <summary>
/// The alignment relative to the size of the object.
/// </summary>
public struct Pivot
{
    /// <summary>
    /// The pivot at the center of the object.
    /// </summary>
    public static readonly Pivot Center = new(0, 0);
    /// <summary>
    /// The pivot at the left edge, vertically centered.
    /// </summary>
    public static readonly Pivot LeftCenter = new(-0.5f, 0);
    /// <summary>
    /// The pivot at the right edge, vertically centered.
    /// </summary>
    public static readonly Pivot RightCenter = new(0.5f, 0);
    /// <summary>
    /// The pivot at the top edge, horizontally centered.
    /// </summary>
    public static readonly Pivot CenterTop = new(0, 0.5f);
    /// <summary>
    /// The pivot at the bottom edge, horizontally centered.
    /// </summary>
    public static readonly Pivot CenterBottom = new(0, -0.5f);
    /// <summary>
    /// The pivot at the top-left corner.
    /// </summary>
    public static readonly Pivot LeftTop = new(-0.5f, 0.5f);
    /// <summary>
    /// The pivot at the bottom-left corner.
    /// </summary>
    public static readonly Pivot LeftBottom = new(-0.5f, -0.5f);
    /// <summary>
    /// The pivot at the top-right corner.
    /// </summary>
    public static readonly Pivot RightTop = new(0.5f, 0.5f);
    /// <summary>
    /// The pivot at the bottom-right corner.
    /// </summary>
    public static readonly Pivot RightBottom = new(0.5f, -0.5f);

    /// <summary>
    /// The X component of the pivot.
    /// </summary>
    public ref float X
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Unsafe.AsRef(in value.X);
    }

    /// <summary>
    /// The Y component of the pivot.
    /// </summary>
    public ref float Y
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref Unsafe.AsRef(in value.Y);
    }

    /// <summary>
    /// Initializes a pivot with normalized coordinates.
    /// </summary>
    /// <param name="x">The horizontal offset from the object's center, in units of its width.</param>
    /// <param name="y">The vertical offset from the object's center, in units of its height.</param>
    public Pivot(float x, float y)
    {
        value = new Vector2(x, y);
    }
    
    public Vector2 value;

    /// <summary>
    /// Converts a vector to a pivot.
    /// </summary>
    /// <param name="v">The vector to convert.</param>
    /// <returns>The converted pivot.</returns>
    public static implicit operator Pivot(Vector2 v) => new(v.X, v.Y);
    /// <summary>
    /// Converts a pivot to its vector representation.
    /// </summary>
    /// <param name="p">The pivot to convert.</param>
    /// <returns>The converted vector.</returns>
    public static implicit operator Vector2(Pivot p) => p.value;
}