using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public class BaseCurve3D<T>:ICurve3D where T: ICurve
    {
        private List<CurvePoint<float3>> _points = new List<CurvePoint<float3>>();

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

        public IReadOnlyList<CurvePoint<float3>> Points
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


        public BaseCurve3D(IList<CurvePoint<float3>> points)
        {
            SetPoints(points);
        }


        public void SetPoints(IList<CurvePoint<float3>> points)
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
                xPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.x));
                yPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.y));
                zPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.z));
            }

            _curveX = (T)Activator.CreateInstance(typeof(T));
            _curveY = (T)Activator.CreateInstance(typeof(T));
            _curveZ = (T)Activator.CreateInstance(typeof(T));

            _curveX.SetPoints(xPoints);
            _curveY.SetPoints(yPoints);
            _curveZ.SetPoints(zPoints);
        }

        public float3 Evaluate(float t)
        {
            float3 result;
            result.x = _curveX.Evaluate(t);
            result.y = _curveY.Evaluate(t);
            result.z = _curveZ.Evaluate(t);
            return result;
        }

    }
}

