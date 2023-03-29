using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using UnityEngine;

namespace Vocore
{
    public class CurveCache : ICurve
    {
        private KeyFrame<float>[] _points;
        private float _step = ConstCurve.DefaultStep;

        public int PointsCount
        {
            get
            {
                return _points.Length;
            }
        }

        public IReadOnlyList<KeyFrame<float>> Points
        {
            get
            {
                return _points;
            }
        }

        public CurveCache(ICurve curve, float step = ConstCurve.DefaultStep)
        {
            CacheCurve(curve, step);
        }

        public void SetPoints(IList<KeyFrame<float>> points)
        {
            //default use linear
            ICurve curve = new CurveLinear(points);
            CacheCurve(curve, _step);
        }

        public void CacheCurve(ICurve curve, float step = ConstCurve.DefaultStep)
        {
            if (curve == null) throw new ArgumentNullException("curve to cache");
            _step = step;
            int count = Mathf.FloorToInt((curve.Points[curve.PointsCount - 1].t - curve.Points[0].t) / step) + 2;

            _points = new KeyFrame<float>[count];
            Parallel.For(0, count - 1, (i) =>
            {
                float t = curve.Points[0].t + i * step;
                float value = curve.Evaluate(t);
                _points[i] = new KeyFrame<float>(t, value);
            });
            _points[count - 1] = new KeyFrame<float>(curve.Points[curve.PointsCount - 1].t, curve.Points[curve.PointsCount - 1].value);
        }

        public float Evaluate(float t)
        {
            t = Mathf.Clamp(t, _points[0].t, _points[_points.Length - 1].t);
            //find the nearest two point by t and step
            int index = Mathf.FloorToInt((t - _points[0].t) / _step);
            int index2 = index + 1;
            //interpolate between two points
            float t1 = _points[index].t;
            float t2 = _points[index2].t;
            float v1 = _points[index].value;
            float v2 = _points[index2].value;

            if (index2 == _points.Length-1)
            {
                return Mathf.Lerp(v1, v2, (t - t1) / (t2 - t1));
            }

            return Mathf.Lerp(v1, v2, (t - t1) / _step);
        }
    }
}

