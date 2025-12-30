using System;
using System.Numerics;

namespace Alco;

public class CurveCache2D : BaseCurveCache<Vector2>
{
    public CurveCache2D(ICurve<Vector2> curve, float step = DefaultStep) : base(curve, step)
    {
    }

    protected override Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        return Vector2.Lerp(a, b, t);
    }
}
