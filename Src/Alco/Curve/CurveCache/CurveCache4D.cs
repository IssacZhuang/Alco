using System;
using System.Numerics;

namespace Alco;

public class CurveCache4D : BaseCurveCache<Vector4>
{
    public CurveCache4D(ICurve<Vector4> curve, float step = DefaultStep) : base(curve, step)
    {
    }

    protected override Vector4 Lerp(Vector4 a, Vector4 b, float t)
    {
        return Vector4.Lerp(a, b, t);
    }
}
