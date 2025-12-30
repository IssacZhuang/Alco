using System;

namespace Alco;

public class CurveCache : BaseCurveCache<float>
{
    public CurveCache(ICurve<float> curve, float step = DefaultStep) : base(curve, step)
    {
    }

    protected override float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }
}
