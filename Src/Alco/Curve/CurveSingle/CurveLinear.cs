using System;
using System.Collections;
using System.Collections.Generic;

namespace Alco;

public sealed class CurveLinear : BaseCurveLinear<float>
{
    public CurveLinear()
    {
    }

    public CurveLinear(ReadOnlySpan<CurvePoint<float>> points) : base(points)
    {
    }

    public CurveLinear(IReadOnlyList<CurvePoint<float>> points) : base(points)
    {
    }

    protected override float Lerp(float a, float b, float t)
    {
        return a + (b - a) * t;
    }
}
