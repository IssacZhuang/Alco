using System;
using System.Numerics;
using System.Collections.Generic;
using System.Buffers;



namespace Alco
{
    public class BaseCurve4D<T>:ICurve4D where T: ICurve, new()
    {
        private readonly List<CurvePoint4Value> _points = new List<CurvePoint4Value>();

        private T _curveX;
        private T _curveY;
        private T _curveZ;
        private T _curveW;

        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IReadOnlyList<CurvePoint4Value> Points
        {
            get
            {
                return _points;
            }
        }

        public BaseCurve4D()
        {
            _curveX = new T();
            _curveY = new T();
            _curveZ = new T();
            _curveW = new T();
        }

        public BaseCurve4D(ReadOnlySpan<CurvePoint4Value> points)
        {
            _curveX = new T();
            _curveY = new T();
            _curveZ = new T();
            _curveW = new T();

            SetPoints(points);
        }

        public void SetPoints(ReadOnlySpan<CurvePoint4Value> points)
        {
            _points.Clear();

            int length = points.Length;

            CurvePointValue[] xPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);
            CurvePointValue[] yPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);
            CurvePointValue[] zPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);
            CurvePointValue[] wPoints = ArrayPool<CurvePointValue>.Shared.Rent(length);

            for (int i = 0; i < length; i++)
            {
                _points.Add(points[i]);
                xPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.X);
                yPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.Y);
                zPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.Z);
                wPoints[i] = new CurvePointValue(points[i].Time, points[i].Value.W);
            }

            _curveX.SetPoints(xPoints.AsSpan(0, length));
            _curveY.SetPoints(yPoints.AsSpan(0, length));
            _curveZ.SetPoints(zPoints.AsSpan(0, length));
            _curveW.SetPoints(wPoints.AsSpan(0, length));

            ArrayPool<CurvePointValue>.Shared.Return(xPoints);
            ArrayPool<CurvePointValue>.Shared.Return(yPoints);
            ArrayPool<CurvePointValue>.Shared.Return(zPoints);
            ArrayPool<CurvePointValue>.Shared.Return(wPoints);
        }

        public Vector4 Evaluate(float t)
        {
            Vector4 result;
            result.X = _curveX.Evaluate(t);
            result.Y = _curveY.Evaluate(t);
            result.Z = _curveZ.Evaluate(t);
            result.W = _curveW.Evaluate(t);
            return result;
        }
    }
}

