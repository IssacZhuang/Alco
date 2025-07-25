using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    public class BaseCurve4D<T>:ICurve4D where T: ICurve, new()
    {
        private List<CurvePoint4Value> _points = new List<CurvePoint4Value>();

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

        public BaseCurve4D(IReadOnlyList<CurvePoint4Value> points)
        {
            _curveX = new T();
            _curveY = new T();
            _curveZ = new T();
            _curveW = new T();

            SetPoints(points);
        }

        public void SetPoints(IReadOnlyList<CurvePoint4Value> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }

            _points.Clear();

            List<CurvePointValue> xPoints = new List<CurvePointValue>();
            List<CurvePointValue> yPoints = new List<CurvePointValue>();
            List<CurvePointValue> zPoints = new List<CurvePointValue>();
            List<CurvePointValue> wPoints = new List<CurvePointValue>();

            for (int i = 0; i < points.Count; i++)
            {
                xPoints.Add(new CurvePointValue(points[i].Time, points[i].Value.X));
                yPoints.Add(new CurvePointValue(points[i].Time, points[i].Value.Y));
                zPoints.Add(new CurvePointValue(points[i].Time, points[i].Value.Z));
                wPoints.Add(new CurvePointValue(points[i].Time, points[i].Value.W));
            }

            _curveX.SetPoints(xPoints);
            _curveY.SetPoints(yPoints);
            _curveZ.SetPoints(zPoints);
            _curveW.SetPoints(wPoints);
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

