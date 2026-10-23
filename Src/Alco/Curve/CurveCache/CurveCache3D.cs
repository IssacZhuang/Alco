using System;
using System.Numerics;

namespace Alco;

/// <summary>
/// Caches samples of a 3D curve at a fixed step.
/// </summary>
public class CurveCache3D : BaseCurveCache<Vector3>
{
    /// <summary>
    /// Initializes a cache for the given curve.
    /// </summary>
    /// <param name="curve">The curve to cache.</param>
    /// <param name="step">The sampling step along the curve.</param>
    public CurveCache3D(ICurve<Vector3> curve, float step = DefaultStep) : base(curve, step)
    {
    }

    protected override Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return Vector3.Lerp(a, b, t);
    }
}
