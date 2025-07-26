using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;


namespace Alco
{
    public class CurveCache3D : ICurve3D
    {
        private CurvePoint3Value[] _points;
        private readonly float _step = ConstCurve.DefaultStep;

        public int PointsCount
        {
            get
            {
                return _points.Length;
            }
        }

        public IReadOnlyList<CurvePoint3Value> Points
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

        public void SetPoints(ReadOnlySpan<CurvePoint3Value> points)
        {
            //default use linear
            ICurve3D curve = new CurveLinear3D(points);
            _points = CacheCurve(curve, _step);
        }



        public Vector3 Evaluate(float t)
        {

            t = math.clamp(t, _points[0].Time, _points[_points.Length - 1].Time);
            //find the nearest two point by t and step
            int index = (int)math.floor((t - _points[0].Time) / _step);
            int index2 = index + 1;
            //interpolate between two points
            float t1 = _points[index].Time;
            float t2 = _points[index2].Time;
            Vector3 v1 = _points[index].Value;
            Vector3 v2 = _points[index2].Value;

            if (index2 == _points.Length - 1)
            {
                return math.lerp(v1, v2, (t - t1) / (t2 - t1));
            }

            return math.lerp(v1, v2, (t - t1) / _step);
        }

        public static CurvePoint3Value[] CacheCurve(ICurve3D curve, float step)
        {
            if (curve == null) throw new ArgumentNullException(nameof(curve));

            int count = (int)math.floor((curve.Points[curve.PointsCount - 1].Time - curve.Points[0].Time) / step) + 2;

            CurvePoint3Value[] points = new CurvePoint3Value[count];
            Parallel.For(0, count - 1, (i) =>
            {
                float t = curve.Points[0].Time + i * step;
                Vector3 value = curve.Evaluate(t);

                points[i] = new CurvePoint3Value(t, value);
            });
            points[count - 1] = new CurvePoint3Value(curve.Points[curve.PointsCount - 1].Time, curve.Points[curve.PointsCount - 1].Value);

            return points;
        }
    }
}

