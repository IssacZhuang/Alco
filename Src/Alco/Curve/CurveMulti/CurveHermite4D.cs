using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

/// <summary>
/// A Hermite curve of 4D vectors.
/// </summary>
public class CurveHermite4D : BaseCurveHermite<Vector4>
{
    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public CurveHermite4D()
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveHermite4D(ReadOnlySpan<CurvePoint<Vector4>> points) : base(points)
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveHermite4D(IReadOnlyList<CurvePoint<Vector4>> points) : base(points)
    {
    }

    protected override Vector4 Subtract(Vector4 a, Vector4 b)
    {
        return a - b;
    }

    protected override Vector4 Add(Vector4 a, Vector4 b)
    {
        return a + b;
    }

    protected override Vector4 Scale(Vector4 a, float scalar)
    {
        return a * scalar;
    }
}

