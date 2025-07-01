using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    public class BaseCurve4D<T>:ICurve4D where T: ICurve, new()
    {
        private List<CurvePoint4> _points = new List<CurvePoint4>();

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

        public IReadOnlyList<CurvePoint4> Points
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

        public BaseCurve4D(IReadOnlyList<CurvePoint4> points)
        {
            _curveX = new T();
            _curveY = new T();
            _curveZ = new T();
            _curveW = new T();

            SetPoints(points);
        }

        public void SetPoints(IReadOnlyList<CurvePoint4> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }

            _points.Clear();

            List<CurvePoint> xPoints = new List<CurvePoint>();
            List<CurvePoint> yPoints = new List<CurvePoint>();
            List<CurvePoint> zPoints = new List<CurvePoint>();
            List<CurvePoint> wPoints = new List<CurvePoint>();

            for (int i = 0; i < points.Count; i++)
            {
                xPoints.Add(new CurvePoint(points[i].Time, points[i].Value.X));
                yPoints.Add(new CurvePoint(points[i].Time, points[i].Value.Y));
                zPoints.Add(new CurvePoint(points[i].Time, points[i].Value.Z));
                wPoints.Add(new CurvePoint(points[i].Time, points[i].Value.W));
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

