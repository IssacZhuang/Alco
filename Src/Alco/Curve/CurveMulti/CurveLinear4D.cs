using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

public class CurveLinear4D : BaseCurveLinear<Vector4>
{
    public CurveLinear4D()
    {
    }

    public CurveLinear4D(ReadOnlySpan<CurvePoint<Vector4>> points) : base(points)
    {
    }

    public CurveLinear4D(IReadOnlyList<CurvePoint<Vector4>> points) : base(points)
    {
    }

    protected override Vector4 Lerp(Vector4 a, Vector4 b, float t)
    {
        return Vector4.Lerp(a, b, t);
    }
}


