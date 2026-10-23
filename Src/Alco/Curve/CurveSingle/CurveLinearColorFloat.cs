using System;
using System.Collections.Generic;

namespace Alco;

/// <summary>
/// A piecewise linear curve of floating-point colors.
/// </summary>
public class CurveLinearColorFloat : BaseCurveLinear<ColorFloat>
{
    /// <summary>
    /// Initializes an empty curve.
    /// </summary>
    public CurveLinearColorFloat()
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinearColorFloat(ReadOnlySpan<CurvePoint<ColorFloat>> points) : base(points)
    {
    }

    /// <summary>
    /// Initializes a curve from the given control points.
    /// </summary>
    /// <param name="points">The control points of the curve.</param>
    public CurveLinearColorFloat(IReadOnlyList<CurvePoint<ColorFloat>> points) : base(points)
    {
    }

    protected override ColorFloat Lerp(ColorFloat a, ColorFloat b, float t)
    {
        return ColorFloat.Lerp(a, b, t);
    }
}







