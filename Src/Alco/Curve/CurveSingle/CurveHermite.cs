using System;
using System.Collections.Generic;

namespace Alco;

public class CurveHermite : BaseCurveHermite<float>
{
    public CurveHermite()
    {
    }

    public CurveHermite(ReadOnlySpan<CurvePoint<float>> points) : base(points)
    {
    }

    public CurveHermite(IReadOnlyList<CurvePoint<float>> points) : base(points)
    {
    }

    protected override float Subtract(float a, float b)
    {
        return a - b;
    }

    protected override float Add(float a, float b)
    {
        return a + b;
    }

    protected override float Scale(float a, float scalar)
    {
        return a * scalar;
    }
}
