using System;
using System.Collections.Generic;

namespace Alco;

public class CurveLinearColorFloat : BaseCurveLinear<ColorFloat>
{
    public CurveLinearColorFloat()
    {
    }

    public CurveLinearColorFloat(ReadOnlySpan<CurvePoint<ColorFloat>> points) : base(points)
    {
    }

    public CurveLinearColorFloat(IReadOnlyList<CurvePoint<ColorFloat>> points) : base(points)
    {
    }

    protected override ColorFloat Lerp(ColorFloat a, ColorFloat b, float t)
    {
        return ColorFloat.Lerp(a, b, t);
    }
}

