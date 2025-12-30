using System;
using System.Collections.Generic;

namespace Alco;

public class CurveLinearColor32 : BaseCurveLinear<Color32>
{
    public CurveLinearColor32()
    {
    }

    public CurveLinearColor32(ReadOnlySpan<CurvePoint<Color32>> points) : base(points)
    {
    }

    public CurveLinearColor32(IReadOnlyList<CurvePoint<Color32>> points) : base(points)
    {
    }

    protected override Color32 Lerp(Color32 a, Color32 b, float t)
    {
        return Color32.Lerp(a, b, t);
    }
}

