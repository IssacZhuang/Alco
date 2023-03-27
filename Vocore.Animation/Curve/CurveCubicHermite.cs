using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore.Animation
{
    public class CurveCubicHermite : ICurve
    {

        private List<KeyFrame> _points;
        private float[] _slopes;

        public int PointsCount
        {
            get
            {
                return this._points.Count;
            }
        }

        public IList<KeyFrame> Points
        {
            get
            {
                return this._points;
            }
        }

        public CurveCubicHermite(List<KeyFrame> nodes)
        {
            _points = nodes;
            SortPoints();
            RefreshSlopes();
        }

        public CurveCubicHermite(float[] x, float[] y)
        {
            _points = new List<KeyFrame>();
            for (int i = 0; i < x.Length; i++)
            {
                _points.Add(new KeyFrame(x[i], y[i]));
            }
            SortPoints();
            RefreshSlopes();
        }

        public float Evaluate(float t)
        {
            if (t <= this._points[0].t)
			{
				return this._points[0].value;
			}
			if (t >= this._points[this._points.Count - 1].t)
			{
				return this._points[this._points.Count - 1].value;
			}
            int index = 0;
            int count = _points.Count;

            for (int i = 1; i < count; i++)
            {
                if (_points[i].t > t)
                {
                    index = i - 1;
                    break;
                }
            }

            float t0 = _points[index].t;
            float t1 = _points[index + 1].t;
            float p0 = _points[index].value;
            float p1 = _points[index + 1].value;
            float m0 = _slopes[index];
            float m1 = _slopes[index + 1];
            float dt = t1 - t0;
            float tNormalized = (t - t0) / dt;

            // Cubic Hermite Spline
            float h00 = 2 * Mathf.Pow(tNormalized, 3) - 3 * Mathf.Pow(tNormalized, 2) + 1;
            float h10 = Mathf.Pow(tNormalized, 3) - 2 * Mathf.Pow(tNormalized, 2) + tNormalized;
            float h01 = -2 * Mathf.Pow(tNormalized, 3) + 3 * Mathf.Pow(tNormalized, 2);
            float h11 = Mathf.Pow(tNormalized, 3) - Mathf.Pow(tNormalized, 2);

            float interpolatedValue = h00 * p0 + h10 * dt * m0 + h01 * p1 + h11 * dt * m1;
            return interpolatedValue;
        }

        public void SortPoints()
        {
            _points.Sort((a, b) => a.t.CompareTo(b.t));
        }

        public void RefreshSlopes()
        {
            _slopes = CalculateSlopes(_points);
        }

        private static float[] CalculateSlopes(IList<KeyFrame> points)
        {
            int count = points.Count;
            float[] slopes = new float[count];

            for (int i = 0; i < count; i++)
            {
                if (i == 0)
                {
                    slopes[i] = (points[i + 1].value - points[i].value) / (points[i + 1].t - points[i].t);
                }
                else if (i == count - 1)
                {
                    slopes[i] = (points[i].value - points[i - 1].value) / (points[i].t - points[i - 1].t);
                }
                else
                {
                    float dydx1 = (points[i].value - points[i - 1].value) / (points[i].t - points[i - 1].t);
                    float dydx2 = (points[i + 1].value - points[i].value) / (points[i + 1].t - points[i].t);
                    slopes[i] = (dydx1 + dydx2) / 2f;
                }
            }

            return slopes;
        }
    }
}

