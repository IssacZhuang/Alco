using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

/// <summary>
/// A piecewise linear curve of 3D vectors.
/// </summary>
public class CurveLinear3D : BaseCurveLinear<Vector3>
{
    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public CurveLinear3D()
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinear3D(ReadOnlySpan<CurvePoint<Vector3>> points) : base(points)
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinear3D(IReadOnlyList<CurvePoint<Vector3>> points) : base(points)
    {
    }

    protected override Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return Vector3.Lerp(a, b, t);
    }
}


