using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

public class CurveHermite4D : BaseCurveHermite<Vector4>
{
    public CurveHermite4D()
    {
    }

    public CurveHermite4D(ReadOnlySpan<CurvePoint<Vector4>> points) : base(points)
    {
    }

    public CurveHermite4D(IReadOnlyList<CurvePoint<Vector4>> points) : base(points)
    {
    }

    protected override Vector4 Subtract(Vector4 a, Vector4 b)
    {
        return a - b;
    }

    protected override Vector4 Add(Vector4 a, Vector4 b)
    {
        return a + b;
    }

    protected override Vector4 Scale(Vector4 a, float scalar)
    {
        return a * scalar;
    }
}

