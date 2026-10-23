using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

/// <summary>
/// A Hermite curve of 2D vectors.
/// </summary>
public class CurveHermite2D : BaseCurveHermite<Vector2>
{
    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public CurveHermite2D()
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveHermite2D(ReadOnlySpan<CurvePoint<Vector2>> points) : base(points)
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveHermite2D(IReadOnlyList<CurvePoint<Vector2>> points) : base(points)
    {
    }

    protected override Vector2 Subtract(Vector2 a, Vector2 b)
    {
        return a - b;
    }

    protected override Vector2 Add(Vector2 a, Vector2 b)
    {
        return a + b;
    }

    protected override Vector2 Scale(Vector2 a, float scalar)
    {
        return a * scalar;
    }
}

