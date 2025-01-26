using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    public class BaseCurve4D<T>:ICurve4D where T: ICurve, new()
    {
        private List<CurvePoint<Vector4>> _points = new List<CurvePoint<Vector4>>();

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

        public IReadOnlyList<CurvePoint<Vector4>> Points
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

        public BaseCurve4D(IReadOnlyList<CurvePoint<Vector4>> points)
        {
            _curveX = new T();
            _curveY = new T();
            _curveZ = new T();
            _curveW = new T();

            SetPoints(points);
        }

        public void SetPoints(IReadOnlyList<CurvePoint<Vector4>> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }

            _points.Clear();

            List<CurvePoint<float>> xPoints = new List<CurvePoint<float>>();
            List<CurvePoint<float>> yPoints = new List<CurvePoint<float>>();
            List<CurvePoint<float>> zPoints = new List<CurvePoint<float>>();
            List<CurvePoint<float>> wPoints = new List<CurvePoint<float>>();

            for (int i = 0; i < points.Count; i++)
            {
                xPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.X));
                yPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.Y));
                zPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.Z));
                wPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.W));
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

