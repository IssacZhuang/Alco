using System;
using System.Collections.Generic;
using UnityEngine;

namespace Vocore
{
    public class BaseCurve2D<T> : ICurve2D where T : ICurve
    {
        private List<KeyFrame<Vector2>> _points = new List<KeyFrame<Vector2>>();

        private T _curveX;
        private T _curveY;

        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IList<KeyFrame<Vector2>> Points
        {
            get
            {
                return _points;
            }
        }

        public BaseCurve2D()
        {
        }

        public BaseCurve2D(IList<KeyFrame<Vector2>> points)
        {
            SetPoints(points);
        }

        public void SetPoints(IList<KeyFrame<Vector2>> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }

            _points.Clear();

            List<KeyFrame<float>> xPoints = new List<KeyFrame<float>>();
            List<KeyFrame<float>> yPoints = new List<KeyFrame<float>>();

            for (int i = 0; i < points.Count; i++)
            {
                _points.Add(points[i]);
                xPoints.Add(new KeyFrame<float>(points[i].t, points[i].value.x));
                yPoints.Add(new KeyFrame<float>(points[i].t, points[i].value.y));
            }

            _curveX = (T)Activator.CreateInstance(typeof(T));
            _curveY = (T)Activator.CreateInstance(typeof(T));

            _curveX.SetPoints(xPoints);
            _curveY.SetPoints(yPoints);
        }

        public Vector2 Evaluate(float t)
        {
            Vector2 result;
            result.x = _curveX.Evaluate(t);
            result.y = _curveY.Evaluate(t);
            return result;
        }
    }
}

