using System;

namespace Alco;

/// <summary>
/// Caches samples of a scalar curve at a fixed step.
/// </summary>
public class CurveCache : BaseCurveCache<float>
{
    /// <summary>
    /// Initializes a cache for the given curve.
    /// </summary>
    /// <param name="curve">The curve to cache.</param>
    /// <param name="step">The sampling step along the curve.</param>
    public CurveCache(ICurve<float> curve, float step = DefaultStep) : base(curve, step)
    {
    }

    protected override float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }
}
