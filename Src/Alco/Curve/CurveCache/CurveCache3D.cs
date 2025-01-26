using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;


namespace Alco
{
    public class CurveCache3D : ICurve3D
    {
        private CurvePoint<Vector3>[] _points;
        private readonly float _step = ConstCurve.DefaultStep;

        public int PointsCount
        {
            get
            {
                return _points.Length;
            }
        }

        public IReadOnlyList<CurvePoint<Vector3>> Points
        {
            get
            {
                return _points;
            }
        }

        public CurveCache3D(ICurve3D curve, float step = ConstCurve.DefaultStep)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step));

            _step = step;
            _points = CacheCurve(curve, step);
        }

        public void SetPoints(IReadOnlyList<CurvePoint<Vector3>> points)
        {
            //default use linear
            ICurve3D curve = new CurveLinear3D(points);
            _points = CacheCurve(curve, _step);
        }



        public Vector3 Evaluate(float t)
        {

            t = math.clamp(t, _points[0].t, _points[_points.Length - 1].t);
            //find the nearest two point by t and step
            int index = (int)math.floor((t - _points[0].t) / _step);
            int index2 = index + 1;
            //interpolate between two points
            float t1 = _points[index].t;
            float t2 = _points[index2].t;
            Vector3 v1 = _points[index].value;
            Vector3 v2 = _points[index2].value;

            if (index2 == _points.Length - 1)
            {
                return math.lerp(v1, v2, (t - t1) / (t2 - t1));
            }

            return math.lerp(v1, v2, (t - t1) / _step);
        }

        public static CurvePoint<Vector3>[] CacheCurve(ICurve3D curve, float step)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));

            int count = (int)math.floor((curve.Points[curve.PointsCount - 1].t - curve.Points[0].t) / step) + 2;

            CurvePoint<Vector3>[] points = new CurvePoint<Vector3>[count];
            Parallel.For(0, count - 1, (i) =>
            {
                float t = curve.Points[0].t + i * step;
                Vector3 value = curve.Evaluate(t);

                points[i] = new CurvePoint<Vector3>(t, value);
            });
            points[count - 1] = new CurvePoint<Vector3>(curve.Points[curve.PointsCount - 1].t, curve.Points[curve.PointsCount - 1].value);

            return points;
        }
    }
}

