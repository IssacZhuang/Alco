using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vocore
{
    public class CurveLinear2D: BaseCurve2D<CurveLinear>
    {
        public CurveLinear2D():base()
        {
            
        }

        public CurveLinear2D(IList<CurvePoint<Vector2>> points) :base(points)
        {
            
        }
    }
}

