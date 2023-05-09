using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public class CurveCache4D:ICurve4D
    {
        private List<CurvePoint<float4>> _points;
        private float _step = ConstCurve.DefaultStep;

        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IReadOnlyList<CurvePoint<float4>> Points
        {
            get
            {
                return _points;
            }
        }

        public CurveCache4D(ICurve4D curve, float step = ConstCurve.DefaultStep)
        {
            CacheCurve(curve, step);
        }

        public void SetPoints(IList<CurvePoint<float4>> points)
        {
            //default use linear
            ICurve4D curve = new CurveLinear4D();
            curve.SetPoints(points);
            CacheCurve(curve, _step);
        }

        public void CacheCurve(ICurve4D curve, float step = ConstCurve.DefaultStep)
        {
            if (curve == null) throw ExceptionCurve.NullCurve;

            _points = new List<CurvePoint<float4>>();
            _step = step;
            //evaluate curve by step and cache the result
            for (float t = curve.Points[0].t; t < curve.Points[curve.PointsCount - 1].t; t += step)
            {
                _points.Add(new CurvePoint<float4>(t, curve.Evaluate(t)));
            }
            _points.Add(new CurvePoint<float4>(curve.Points[curve.PointsCount - 1].t, curve.Evaluate(curve.Points[curve.PointsCount - 1].t)));
        }

        public float4 Evaluate(float t)
        {
            t = math.clamp(t, _points[0].t, _points[_points.Count - 1].t);
            //find the nearest two point by t and step
            int index = (int)math.floor((t - _points[0].t) / _step);
            int index2 = index + 1;
            //interpolate between two points
            float t1 = _points[index].t;
            float t2 = _points[index2].t;
            float t3 = (t - t1) / (t2 - t1);
            return math.lerp(_points[index].value, _points[index2].value, t3);
        }
    }
}

