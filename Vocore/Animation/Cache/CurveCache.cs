using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public class CurveCache : ICurve
    {
        public const float DefaultStep = 1 / 60f;
        private List<KeyFrame<float>> _points;
        private float step = DefaultStep;

        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IList<KeyFrame<float>> Points
        {
            get
            {
                return _points;
            }
        }

        public CurveCache(ICurve curve, float step = DefaultStep)
        {
            CacheCurve(curve, step);
        }

        public void SetPoints(IList<KeyFrame<float>> points)
        {
            //default use linear
            ICurve curve = new CurveLinear(points);
            CacheCurve(curve, step);
        }

        public void CacheCurve(ICurve curve, float step = DefaultStep)
        {
            if (curve == null) throw new ArgumentNullException("curve to cache");

            _points = new List<KeyFrame<float>>();
            this.step = step;
            //evaluate curve by step and cache the result
            for (float t = curve.Points[0].t; t < curve.Points[curve.PointsCount - 1].t; t += step)
            {
                _points.Add(new KeyFrame<float>(t, curve.Evaluate(t)));
            }
            _points.Add(new KeyFrame<float>(curve.Points[curve.PointsCount - 1].t, curve.Evaluate(curve.Points[curve.PointsCount - 1].t)));
        }

        public float Evaluate(float t)
        {
            t = Mathf.Clamp(t, _points[0].t, _points[_points.Count - 1].t);
            //find the nearest two point by t and step
            int index = Mathf.FloorToInt((t - _points[0].t) / step);
            int index2 = index + 1;

            //interpolate between two points
            float t1 = _points[index].t;
            float t2 = _points[index2].t;
            float v1 = _points[index].value;
            float v2 = _points[index2].value;
            return Mathf.Lerp(v1, v2, (t - t1) / (t2 - t1));
        }
    }
}

