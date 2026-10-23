using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

/// <summary>
/// A piecewise linear curve of 2D vectors.
/// </summary>
public class CurveLinear2D : BaseCurveLinear<Vector2>
{
    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public CurveLinear2D()
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinear2D(ReadOnlySpan<CurvePoint<Vector2>> points) : base(points)
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinear2D(IReadOnlyList<CurvePoint<Vector2>> points) : base(points)
    {
    }

    protected override Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        return Vector2.Lerp(a, b, t);
    }
}
