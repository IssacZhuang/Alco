using System;
using System.Numerics;
using System.Collections.Generic;
using System.Buffers;


namespace Alco
{
    public class BaseCurve3D<T> : ICurve3D where T : ICurve, new()
    {
        private readonly List<CurvePoint3Value> _points = new List<CurvePoint3Value>();

        private T _curveX;
        private T _curveY;
        private T _curveZ;

        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IReadOnlyList<CurvePoint3Value> Points
        {
            get
            {
                return _points;
            }
        }

        public BaseCurve3D()
        {
            _curveX = new T();
            _curveY = new T();
            _curveZ = new T();
        }

        public BaseCurve3D(ReadOnlySpan<CurvePoint3Value> points)
        {
            _curveX = new T();
            _curveY = new T();
            _curveZ = new T();

            SetPoints(points);
        }

        public void SetPoints(ReadOnlySpan<CurvePoint3Value> points)
        {
            _points.Clear();

            int length = points.Length;

            CurvePointValue[] xPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);
            CurvePointValue[] yPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);
            CurvePointValue[] zPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);

            for (int i = 0; i < length; i++)
            {
                _points.Add(points[i]);
                xPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.X);
                yPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.Y);
                zPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.Z);
            }

            _curveX.SetPoints(xPoints.AsSpan(0, length));
            _curveY.SetPoints(yPoints.AsSpan(0, length));
            _curveZ.SetPoints(zPoints.AsSpan(0, length));

            ArrayPool<CurvePointValue>.Shared.Return(xPoints);
            ArrayPool<CurvePointValue>.Shared.Return(yPoints);
            ArrayPool<CurvePointValue>.Shared.Return(zPoints);
        }

        public Vector3 Evaluate(float t)
        {
            Vector3 result;
            result.X = _curveX.Evaluate(t);
            result.Y = _curveY.Evaluate(t);
            result.Z = _curveZ.Evaluate(t);
            return result;
        }
    }
}

