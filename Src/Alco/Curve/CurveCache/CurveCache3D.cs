using System;
using System.Numerics;

namespace Alco;

public class CurveCache3D : BaseCurveCache<Vector3>
{
    public CurveCache3D(ICurve<Vector3> curve, float step = DefaultStep) : base(curve, step)
    {
    }

    protected override Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return Vector3.Lerp(a, b, t);
    }
}
