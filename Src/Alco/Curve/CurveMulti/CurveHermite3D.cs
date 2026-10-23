using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

/// <summary>
/// A Hermite curve of 3D vectors.
/// </summary>
public class CurveHermite3D : BaseCurveHermite<Vector3>
{
    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public CurveHermite3D()
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveHermite3D(ReadOnlySpan<CurvePoint<Vector3>> points) : base(points)
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveHermite3D(IReadOnlyList<CurvePoint<Vector3>> points) : base(points)
    {
    }

    protected override Vector3 Subtract(Vector3 a, Vector3 b)
    {
        return a - b;
    }

    protected override Vector3 Add(Vector3 a, Vector3 b)
    {
        return a + b;
    }

    protected override Vector3 Scale(Vector3 a, float scalar)
    {
        return a * scalar;
    }
}

