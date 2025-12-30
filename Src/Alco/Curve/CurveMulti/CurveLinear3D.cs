using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco;

public class CurveLinear3D : BaseCurveLinear<Vector3>
{
    public CurveLinear3D()
    {
    }

    public CurveLinear3D(ReadOnlySpan<CurvePoint<Vector3>> points) : base(points)
    {
    }

    public CurveLinear3D(IReadOnlyList<CurvePoint<Vector3>> points) : base(points)
    {
    }

    protected override Vector3 Lerp(Vector3 a, Vector3 b, float t)
    {
        return Vector3.Lerp(a, b, t);
    }
}


