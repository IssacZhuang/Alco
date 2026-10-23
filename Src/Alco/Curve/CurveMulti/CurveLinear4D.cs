using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

/// <summary>
/// A piecewise linear curve of 4D vectors.
/// </summary>
public class CurveLinear4D : BaseCurveLinear<Vector4>
{
    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public CurveLinear4D()
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinear4D(ReadOnlySpan<CurvePoint<Vector4>> points) : base(points)
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinear4D(IReadOnlyList<CurvePoint<Vector4>> points) : base(points)
    {
    }

    protected override Vector4 Lerp(Vector4 a, Vector4 b, float t)
    {
        return Vector4.Lerp(a, b, t);
    }
}


