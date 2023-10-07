using System;
using System.Numerics;
using System.Collections.Generic;



namespace Vocore
{
    public class BaseCurve3D<T>:ICurve3D where T: ICurve
    {
        private List<CurvePoint<Vector3>> _points = new List<CurvePoint<Vector3>>();

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
            _curveX = (T)Activator.CreateInstance(typeof(T));
            _curveY = (T)Activator.CreateInstance(typeof(T));
            _curveZ = (T)Activator.CreateInstance(typeof(T));
        }


        public BaseCurve3D(IList<CurvePoint<Vector3>> points)
        {
            SetPoints(points);
        }


        public void SetPoints(IList<CurvePoint<Vector3>> points)
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
                xPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.X));
                yPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.Y));
                zPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.Z));
            }

            _curveX = (T)Activator.CreateInstance(typeof(T));
            _curveY = (T)Activator.CreateInstance(typeof(T));
            _curveZ = (T)Activator.CreateInstance(typeof(T));

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

