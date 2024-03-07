using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;


namespace Vocore
{
    public class CurveCache4D : ICurve4D
    {
        private CurvePoint<Vector4>[] _points;
        private readonly float _step = ConstCurve.DefaultStep;

        public int PointsCount
        {
            get
            {
                return _points.Length;
            }
        }

        public IReadOnlyList<CurvePoint<Vector4>> Points
        {
            get
            {
                return _points;
            }
        }

        public CurveCache4D(ICurve4D curve, float step = ConstCurve.DefaultStep)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));

            _step = step;
            _points = CacheCurve(curve, step);
        }

        public void SetPoints(IReadOnlyList<CurvePoint<Vector4>> points)
        {
            //default use linear
            ICurve4D curve = new CurveLinear4D(points);
            _points = CacheCurve(curve, _step);
        }



        public Vector4 Evaluate(float t)
        {

            t = math.clamp(t, _points[0].t, _points[_points.Length - 1].t);
            //find the nearest two point by t and step
            int index = (int)math.floor((t - _points[0].t) / _step);
            int index2 = index + 1;
            //interpolate between two points
            float t1 = _points[index].t;
            float t2 = _points[index2].t;
            Vector4 v1 = _points[index].value;
            Vector4 v2 = _points[index2].value;

            if (index2 == _points.Length - 1)
            {
                return math.lerp(v1, v2, (t - t1) / (t2 - t1));
            }

            return math.lerp(v1, v2, (t - t1) / _step);
        }

        public static CurvePoint<Vector4>[] CacheCurve(ICurve4D curve, float step)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));

            int count = (int)math.floor((curve.Points[curve.PointsCount - 1].t - curve.Points[0].t) / step) + 2;

            CurvePoint<Vector4>[] points = new CurvePoint<Vector4>[count];
            Parallel.For(0, count - 1, (i) =>
            {
                float t = curve.Points[0].t + i * step;
                Vector4 value = curve.Evaluate(t);

                points[i] = new CurvePoint<Vector4>(t, value);
            });
            points[count - 1] = new CurvePoint<Vector4>(curve.Points[curve.PointsCount - 1].t, curve.Points[curve.PointsCount - 1].value);

            return points;
        }
    }
}

