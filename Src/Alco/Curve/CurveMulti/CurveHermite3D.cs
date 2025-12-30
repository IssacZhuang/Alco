using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

public class CurveHermite3D : BaseCurveHermite<Vector3>
{
    public CurveHermite3D()
    {
    }

    public CurveHermite3D(ReadOnlySpan<CurvePoint<Vector3>> points) : base(points)
    {
    }

    public CurveHermite3D(IReadOnlyList<CurvePoint<Vector3>> points) : base(points)
    {
    }

    protected override Vector3 Subtract(Vector3 a, Vector3 b)
    {
        return a - b;
    }

    protected override Vector3 Add(Vector3 a, Vector3 b)
    {
        return a + b;
    }

    protected override Vector3 Scale(Vector3 a, float scalar)
    {
        return a * scalar;
    }
}

