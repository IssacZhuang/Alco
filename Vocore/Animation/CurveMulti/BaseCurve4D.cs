using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public class BaseCurve4D<T>:ICurve4D where T: ICurve
    {
        private List<CurvePoint<float4>> _points = new List<CurvePoint<float4>>();

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

        public IReadOnlyList<CurvePoint<float4>> Points
        {
            get
            {
                return _points;
            }
        }

        public BaseCurve4D()
        {
            _curveX = (T)Activator.CreateInstance(typeof(T));
            _curveY = (T)Activator.CreateInstance(typeof(T));
            _curveZ = (T)Activator.CreateInstance(typeof(T));
            _curveW = (T)Activator.CreateInstance(typeof(T));
        }

        public BaseCurve4D(IList<CurvePoint<float4>> points)
        {
            SetPoints(points);
        }

        public void SetPoints(IList<CurvePoint<float4>> points)
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
                xPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.x));
                yPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.y));
                zPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.z));
                wPoints.Add(new CurvePoint<float>(points[i].t, points[i].value.w));
            }

            _curveX.SetPoints(xPoints);
            _curveY.SetPoints(yPoints);
            _curveZ.SetPoints(zPoints);
            _curveW.SetPoints(wPoints);
        }

        public float4 Evaluate(float t)
        {
            float4 result;
            result.x = _curveX.Evaluate(t);
            result.y = _curveY.Evaluate(t);
            result.z = _curveZ.Evaluate(t);
            result.w = _curveW.Evaluate(t);
            return result;
        }
    }
}

