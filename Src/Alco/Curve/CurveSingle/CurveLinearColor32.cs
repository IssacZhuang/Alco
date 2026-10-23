using System;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// A piecewise linear curve of 32-bit colors.
/// </summary>
public class CurveLinearColor32 : BaseCurveLinear<Color32>
{
    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public CurveLinearColor32()
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinearColor32(ReadOnlySpan<CurvePoint<Color32>> points) : base(points)
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinearColor32(IReadOnlyList<CurvePoint<Color32>> points) : base(points)
    {
    }

    protected override Color32 Lerp(Color32 a, Color32 b, float t)
    {
        return Color32.Lerp(a, b, t);
    }
}

