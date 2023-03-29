using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public class CurveCache3D: ICurve3D
    {
        private List<CurvePoint<Vector3>> _points;
        private float _step = ConstCurve.DefaultStep;

        public int PointsCount
        {
            get
            {
                return _points.Count;
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
            CacheCurve(curve, step);
        }

        public void SetPoints(IList<CurvePoint<Vector3>> points)
        {
            //default use linear
            ICurve3D curve = new CurveLinear3D();
            curve.SetPoints(points);
            CacheCurve(curve, _step);
        }

        public void CacheCurve(ICurve3D curve, float step = ConstCurve.DefaultStep)
        {
            if (curve == null) throw ExceptionCurve.NullCurve;

            _points = new List<CurvePoint<Vector3>>();
            _step = step;
            //evaluate curve by step and cache the result
            for (float t = curve.Points[0].t; t < curve.Points[curve.PointsCount - 1].t; t += step)
            {
                _points.Add(new CurvePoint<Vector3>(t, curve.Evaluate(t)));
            }
            _points.Add(new CurvePoint<Vector3>(curve.Points[curve.PointsCount - 1].t, curve.Evaluate(curve.Points[curve.PointsCount - 1].t)));
        }

        public Vector3 Evaluate(float t)
        {
            t = Mathf.Clamp(t, _points[0].t, _points[_points.Count - 1].t);
            //find the nearest two point by t and step
            int index = Mathf.FloorToInt((t - _points[0].t) / _step);
            int index2 = index + 1;
            //interpolate between two points
            float t1 = _points[index].t;
            float t2 = _points[index2].t;
            float t3 = (t - t1) / (t2 - t1);
            return Vector3.Lerp(_points[index].value, _points[index2].value, t3);
        }
    }
}

