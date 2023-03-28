using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public class BaseCurve4D<T>:ICurve4D where T: ICurve
    {
        private List<KeyFrame<Vector4>> _points = new List<KeyFrame<Vector4>>();

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

        public IList<KeyFrame<Vector4>> Points
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

        public BaseCurve4D(IList<KeyFrame<Vector4>> points)
        {
            SetPoints(points);
        }

        public void SetPoints(IList<KeyFrame<Vector4>> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }

            _points.Clear();

            List<KeyFrame<float>> xPoints = new List<KeyFrame<float>>();
            List<KeyFrame<float>> yPoints = new List<KeyFrame<float>>();
            List<KeyFrame<float>> zPoints = new List<KeyFrame<float>>();
            List<KeyFrame<float>> wPoints = new List<KeyFrame<float>>();

            for (int i = 0; i < points.Count; i++)
            {
                xPoints.Add(new KeyFrame<float>(points[i].t, points[i].value.x));
                yPoints.Add(new KeyFrame<float>(points[i].t, points[i].value.y));
                zPoints.Add(new KeyFrame<float>(points[i].t, points[i].value.z));
                wPoints.Add(new KeyFrame<float>(points[i].t, points[i].value.w));
            }

            _curveX.SetPoints(xPoints);
            _curveY.SetPoints(yPoints);
            _curveZ.SetPoints(zPoints);
            _curveW.SetPoints(wPoints);
        }

        public Vector4 Evaluate(float t)
        {
            Vector4 result;
            result.x = _curveX.Evaluate(t);
            result.y = _curveY.Evaluate(t);
            result.z = _curveZ.Evaluate(t);
            result.w = _curveW.Evaluate(t);
            return result;
        }
    }
}

