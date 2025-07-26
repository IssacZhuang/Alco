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

        public BaseCurve2D(IReadOnlyList<CurvePoint2Value> points)
        {
            _curveX = new T();
            _curveY = new T();

            SetPoints(points);
        }

        public void SetPoints(ReadOnlySpan<CurvePoint2Value> points)
        {
            _points.Clear();

            int length = points.Length;

            CurvePointValue[] tempPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);

            // Process X coordinates
            for (int i = 0; i < length; i++)
            {
                _points.Add(points[i]);
                tempPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.X);
            }
            _curveX.SetPoints(tempPoints.AsSpan(0, length));

            // Process Y coordinates
            for (int i = 0; i < length; i++)
            {
                tempPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.Y);
            }
            _curveY.SetPoints(tempPoints.AsSpan(0, length));

            ArrayPool<CurvePointValue>.Shared.Return(tempPoints);
        }

        public void SetPoints(IReadOnlyList<CurvePoint2Value> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            _points.Clear();

            int length = points.Count;

            CurvePointValue[] tempPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);

            // Process X coordinates
            for (int i = 0; i < length; i++)
            {
                _points.Add(points[i]);
                tempPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.X);
            }
            _curveX.SetPoints(tempPoints.AsSpan(0, length));

            // Process Y coordinates
            for (int i = 0; i < length; i++)
            {
                tempPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.Y);
            }
            _curveY.SetPoints(tempPoints.AsSpan(0, length));

            ArrayPool<CurvePointValue>.Shared.Return(tempPoints);
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

