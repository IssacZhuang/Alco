using System;
using System.Numerics;

namespace Alco;

/// <summary>
/// Caches samples of a 4D curve at a fixed step.
/// </summary>
public class CurveCache4D : BaseCurveCache<Vector4>
{
    /// <summary>
    /// Initializes a cache for the given curve.
    /// </summary>
    /// <param name="curve">The curve to cache.</param>
    /// <param name="step">The sampling step along the curve.</param>
    public CurveCache4D(ICurve<Vector4> curve, float step = DefaultStep) : base(curve, step)
    {
    }

    protected override Vector4 Lerp(Vector4 a, Vector4 b, float t)
    {
        return Vector4.Lerp(a, b, t);
    }
}
