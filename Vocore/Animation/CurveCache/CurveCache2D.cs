using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public class CurveCache2D: ICurve2D
    {
        private List<CurvePoint<Vector2>> _points;
        private float _step = ConstCurve.DefaultStep;

        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IReadOnlyList<CurvePoint<Vector2>> Points
        {
            get
            {
                return _points;
            }
        }

        public CurveCache2D(ICurve2D curve, float step = ConstCurve.DefaultStep)
        {
            CacheCurve(curve, step);
        }

        public void SetPoints(IList<CurvePoint<Vector2>> points)
        {
            //default use linear
            ICurve2D curve = new CurveLinear2D();
            curve.SetPoints(points);
            CacheCurve(curve, _step);
        }

        public void CacheCurve(ICurve2D curve, float step = ConstCurve.DefaultStep)
        {
            if (curve == null) throw ExceptionCurve.NullCurve;

            _points = new List<CurvePoint<Vector2>>();
            _step = step;
            //evaluate curve by step and cache the result
            for (float t = curve.Points[0].t; t < curve.Points[curve.PointsCount - 1].t; t += step)
            {
                _points.Add(new CurvePoint<Vector2>(t, curve.Evaluate(t)));
            }
            _points.Add(new CurvePoint<Vector2>(curve.Points[curve.PointsCount - 1].t, curve.Evaluate(curve.Points[curve.PointsCount - 1].t)));
        }

        public Vector2 Evaluate(float t)
        {
            t = Mathf.Clamp(t, _points[0].t, _points[_points.Count - 1].t);
            //find the nearest two point by t and step
            int index = Mathf.FloorToInt((t - _points[0].t) / _step);
            int index2 = index + 1;
            //evaluate the value by linear
            float t1 = _points[index].t;
            float t2 = _points[index2].t;
            Vector2 v1 = _points[index].value;
            Vector2 v2 = _points[index2].value;
            float t0 = (t - t1) / (t2 - t1);
            return Vector2.Lerp(v1, v2, t0);
        }
    }
}

