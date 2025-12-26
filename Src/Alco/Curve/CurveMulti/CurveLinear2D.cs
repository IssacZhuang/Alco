using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

public class CurveLinear2D : BaseCurveLinear<Vector2>
{
    public CurveLinear2D()
    {
    }

    public CurveLinear2D(ReadOnlySpan<CurvePoint<Vector2>> points) : base(points)
    {
    }

    public CurveLinear2D(IReadOnlyList<CurvePoint<Vector2>> points) : base(points)
    {
    }

    protected override Vector2 Lerp(Vector2 a, Vector2 b, float t)
    {
        return Vector2.Lerp(a, b, t);
    }
}
