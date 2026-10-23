using System;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// A Hermite curve of scalar values.
/// </summary>
public class CurveHermite : BaseCurveHermite<float>
{
    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public CurveHermite()
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveHermite(ReadOnlySpan<CurvePoint<float>> points) : base(points)
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveHermite(IReadOnlyList<CurvePoint<float>> points) : base(points)
    {
    }

    protected override float Subtract(float a, float b)
    {
        return a - b;
    }

    protected override float Add(float a, float b)
    {
        return a + b;
    }

    protected override float Scale(float a, float scalar)
    {
        return a * scalar;
    }
}
