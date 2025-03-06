using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    public class BaseCurve3D<T> : ICurve3D where T : ICurve, new()
    {
        private readonly List<CurvePoint<Vector3>> _points = new List<CurvePoint<Vector3>>();

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

        public IReadOnlyList<CurvePoint<Vector3>> Points
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


        public BaseCurve3D(IReadOnlyList<CurvePoint<Vector3>> points)
        {
            _curveX = new T();
            _curveY = new T();
            _curveZ = new T();

            SetPoints(points);
        }


        public void SetPoints(IReadOnlyList<CurvePoint<Vector3>> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }

            _points.Clear();

            List<CurvePoint<float>> xPoints = new List<CurvePoint<float>>();
            List<CurvePoint<float>> yPoints = new List<CurvePoint<float>>();
            List<CurvePoint<float>> zPoints = new List<CurvePoint<float>>();

            for (int i = 0; i < points.Count; i++)
            {
                _points.Add(points[i]);
                xPoints.Add(new CurvePoint<float>(points[i].Time, points[i].Value.X));
                yPoints.Add(new CurvePoint<float>(points[i].Time, points[i].Value.Y));
                zPoints.Add(new CurvePoint<float>(points[i].Time, points[i].Value.Z));
            }

            _curveX.SetPoints(xPoints);
            _curveY.SetPoints(yPoints);
            _curveZ.SetPoints(zPoints);
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

