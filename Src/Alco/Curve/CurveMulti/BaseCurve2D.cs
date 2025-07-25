using System;
using System.Numerics;
using System.Collections.Generic;


namespace Alco
{
    public class BaseCurve2D<T> : ICurve2D where T : ICurve, new()
    {
        private readonly List<CurvePoint2Value> _points = new List<CurvePoint2Value>();

        private T _curveX;
        private T _curveY;

        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IReadOnlyList<CurvePoint2Value> Points
        {
            get
            {
                return _points;
            }
        }

        public BaseCurve2D()
        {
            _curveX = new T();
            _curveY = new T();
        }

        public BaseCurve2D(IReadOnlyList<CurvePoint2Value> points)
        {
            _curveX = new T();
            _curveY = new T();

            SetPoints(points);
        }

        public void SetPoints(IReadOnlyList<CurvePoint2Value> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }

            _points.Clear();

            List<CurvePointValue> xPoints = new List<CurvePointValue>();
            List<CurvePointValue> yPoints = new List<CurvePointValue>();

            for (int i = 0; i < points.Count; i++)
            {
                _points.Add(points[i]);
                xPoints.Add(new CurvePointValue(points[i].Time, points[i].Value.X));
                yPoints.Add(new CurvePointValue(points[i].Time, points[i].Value.Y));
            }

            _curveX.SetPoints(xPoints);
            _curveY.SetPoints(yPoints);
        }

        public Vector2 Evaluate(float t)
        {
            Vector2 result;
            result.X = _curveX.Evaluate(t);
            result.Y = _curveY.Evaluate(t);
            return result;
        }
    }
}

