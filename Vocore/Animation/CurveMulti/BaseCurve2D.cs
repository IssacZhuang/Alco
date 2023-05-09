using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace Vocore
{
    public class BaseCurve2D<T> : ICurve2D where T : ICurve
    {
        private List<CurvePoint<float2>> _points = new List<CurvePoint<float2>>();

        private T _curveX;
        private T _curveY;

        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IReadOnlyList<CurvePoint<float2>> Points
        {
            get
            {
                return _points;
            }
        }

        public BaseCurve2D()
        {
            _curveX = (T)Activator.CreateInstance(typeof(T));
            _curveY = (T)Activator.CreateInstance(typeof(T));
        }

        public BaseCurve2D(IList<CurvePoint<float2>> points)
        {
            SetPoints(points);
        }

        public void SetPoints(IList<CurvePoint<float2>> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }

            _points.Clear();

            List<CurvePoint<float>> xPoints = new List<CurvePoint<float>>();
            List<CurvePoint<float>> yPoints = new List<CurvePoint<float>>();

            for (int i = 0; i < points.Count; i++)
            {
                _points.Add(points[i]);
                xPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.x));
                yPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.y));
            }

            _curveX = (T)Activator.CreateInstance(typeof(T));
            _curveY = (T)Activator.CreateInstance(typeof(T));

            _curveX.SetPoints(xPoints);
            _curveY.SetPoints(yPoints);
        }

        public float2 Evaluate(float t)
        {
            float2 result;
            result.x = _curveX.Evaluate(t);
            result.y = _curveY.Evaluate(t);
            return result;
        }
    }
}

