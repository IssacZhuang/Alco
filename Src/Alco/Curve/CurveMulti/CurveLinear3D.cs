using System;
using System.Collections.Generic;
using System.Numerics;

namespace Alco
{
    public class CurveLinear3D : BaseCurve3D<CurveLinear>
    {
        public CurveLinear3D() : base()
        {

        }

        public CurveLinear3D(IReadOnlyList<CurvePoint3Value> points) : base(points)
        {

        }
    }
}

