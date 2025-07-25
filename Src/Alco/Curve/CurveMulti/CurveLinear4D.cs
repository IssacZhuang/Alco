using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco
{
    public class CurveLinear4D : BaseCurve4D<CurveLinear>
    {
        public CurveLinear4D() : base()
        {

        }

        public CurveLinear4D(IReadOnlyList<CurvePoint4Value> points) : base(points)
        {

        }
    }
}

