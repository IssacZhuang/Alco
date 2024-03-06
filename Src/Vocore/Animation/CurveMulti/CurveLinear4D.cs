using System;
using System.Collections.Generic;
using System.Numerics;

namespace Vocore
{
    public class CurveLinear4D : BaseCurve4D<CurveLinear>
    {
        public CurveLinear4D() : base()
        {

        }

        public CurveLinear4D(IList<CurvePoint<Vector4>> points) : base(points)
        {

        }
    }
}

