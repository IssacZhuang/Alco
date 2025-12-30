using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

public class CurveHermite2D : BaseCurveHermite<Vector2>
{
    public CurveHermite2D()
    {
    }

    public CurveHermite2D(ReadOnlySpan<CurvePoint<Vector2>> points) : base(points)
    {
    }

    public CurveHermite2D(IReadOnlyList<CurvePoint<Vector2>> points) : base(points)
    {
    }

    protected override Vector2 Subtract(Vector2 a, Vector2 b)
    {
        return a - b;
    }

    protected override Vector2 Add(Vector2 a, Vector2 b)
    {
        return a + b;
    }

    protected override Vector2 Scale(Vector2 a, float scalar)
    {
        return a * scalar;
    }
}

