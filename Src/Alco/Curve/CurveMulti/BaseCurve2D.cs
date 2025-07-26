using System;
using System.Numerics;
using System.Collections.Generic;
using System.Buffers;


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

        public BaseCurve2D(ReadOnlySpan<CurvePoint2Value> points)
        {
            _curveX = new T();
            _curveY = new T();

            SetPoints(points);
        }

        public void SetPoints(ReadOnlySpan<CurvePoint2Value> points)
        {
            _points.Clear();

            int length = points.Length;

            CurvePointValue[] xPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);
            CurvePointValue[] yPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);

            for (int i = 0; i < length; i++)
            {
                _points.Add(points[i]);
                xPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.X);
                yPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.Y);
            }

            _curveX.SetPoints(xPoints.AsSpan(0, length));
            _curveY.SetPoints(yPoints.AsSpan(0, length));

            ArrayPool<CurvePointValue>.Shared.Return(xPoints);
            ArrayPool<CurvePointValue>.Shared.Return(yPoints);
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

