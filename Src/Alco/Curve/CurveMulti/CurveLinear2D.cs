using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco
{
    public class CurveLinear2D: BaseCurve2D<CurveLinear>
    {
        public CurveLinear2D():base()
        {
            
        }

        public CurveLinear2D(ReadOnlySpan<CurvePoint2Value> points) : base(points)
        {
            
        }
    }
}

