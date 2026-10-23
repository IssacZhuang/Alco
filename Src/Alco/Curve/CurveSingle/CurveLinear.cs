using System;
using System.Collections;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// A piecewise linear curve of scalar values.
/// </summary>
public sealed class CurveLinear : BaseCurveLinear<float>
{
    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public CurveLinear()
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinear(ReadOnlySpan<CurvePoint<float>> points) : base(points)
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinear(IReadOnlyList<CurvePoint<float>> points) : base(points)
    {
    }

    protected override float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }
}
