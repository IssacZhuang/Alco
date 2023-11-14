using System;
using System.Numerics;
using System.Collections.Generic;


namespace Vocore
{
    public class BaseCurve2D<T> : ICurve2D where T : ICurve
    {
        private List<CurvePoint<Vector2>> _points = new List<CurvePoint<Vector2>>();

        private T _curveX;
        private T _curveY;

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

        public BaseCurve2D()
        {
            _curveX = (T)Activator.CreateInstance(typeof(T));
            _curveY = (T)Activator.CreateInstance(typeof(T));
        }

        public BaseCurve2D(IList<CurvePoint<Vector2>> points)
        {
            SetPoints(points);
        }

        public void SetPoints(IList<CurvePoint<Vector2>> points)
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
                xPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.X));
                yPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.Y));
            }

            _curveX = (T)Activator.CreateInstance(typeof(T));
            _curveY = (T)Activator.CreateInstance(typeof(T));

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

