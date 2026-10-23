using System;
using System.Numerics;

namespace Alco;

/// <summary>
/// Caches samples of a 2D curve at a fixed step.
/// </summary>
public class CurveCache2D : BaseCurveCache<Vector2>
{
    /// <summary>
    /// Initializes a cache for the given curve.
    /// </summary>
    /// <param name="curve">The curve to cache.</param>
    /// <param name="step">The sampling step along the curve.</param>
    public CurveCache2D(ICurve<Vector2> curve, float step = DefaultStep) : base(curve, step)
    {
    }

    protected override Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        return Vector2.Lerp(a, b, t);
    }
}
