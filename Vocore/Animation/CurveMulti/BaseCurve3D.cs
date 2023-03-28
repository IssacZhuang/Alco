using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public class BaseCurve3D<T>:ICurve3D where T: ICurve
    {
        private List<KeyFrame<Vector3>> _points = new List<KeyFrame<Vector3>>();

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

        public IList<KeyFrame<Vector3>> Points
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


        public BaseCurve3D(IList<KeyFrame<Vector3>> points)
        {
            SetPoints(points);
        }


        public void SetPoints(IList<KeyFrame<Vector3>> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }

            _points.Clear();

            List<KeyFrame<float>> xPoints = new List<KeyFrame<float>>();
            List<KeyFrame<float>> yPoints = new List<KeyFrame<float>>();
            List<KeyFrame<float>> zPoints = new List<KeyFrame<float>>();

            for (int i = 0; i < points.Count; i++)
            {
                _points.Add(points[i]);
                xPoints.Add(new KeyFrame<float>(points[i].t, points[i].value.x));
                yPoints.Add(new KeyFrame<float>(points[i].t, points[i].value.y));
                zPoints.Add(new KeyFrame<float>(points[i].t, points[i].value.z));
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
            result.x = _curveX.Evaluate(t);
            result.y = _curveY.Evaluate(t);
            result.z = _curveZ.Evaluate(t);
            return result;
        }

    }
}

