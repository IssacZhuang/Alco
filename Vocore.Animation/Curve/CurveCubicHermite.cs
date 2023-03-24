using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore.Animation
{
    public class CurveCubicHermite : ICurve
    {

        private List<Vector2> _points;
        private float[] _slopes;

        public int PointsCount
        {
            get
            {
                return this._points.Count;
            }
        }

        public IEnumerable<Vector2> Points
        {
            get
            {
                return this._points;
            }
        }

        public CurveCubicHermite(List<Vector2> nodes)
        {
            _points = nodes;
            SortPoints();
            RefreshSlopes();
        }

        public CurveCubicHermite(float[] x, float[] y)
        {
            _points = new List<Vector2>();
            for (int i = 0; i < x.Length; i++)
            {
                _points.Add(new Vector2(x[i], y[i]));
            }
            SortPoints();
            RefreshSlopes();
        }

        public float Evaluate(float t)
        {
            t = Mathf.Clamp(t, _points[0].x, _points[_points.Count - 1].x);
            int index = 0;
            int count = _points.Count;

            for (int i = 1; i < count; i++)
            {
                if (_points[i].x > t)
                {
                    index = i - 1;
                    break;
                }
            }

            float t0 = _points[index].x;
            float t1 = _points[index + 1].x;
            float p0 = _points[index].y;
            float p1 = _points[index + 1].y;
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
            _points.Sort((a, b) => a.x.CompareTo(b.x));
        }

        public void RefreshSlopes()
        {
            _slopes = CalculateSlopes(_points);
        }

        private static float[] CalculateSlopes(IList<Vector2> points)
        {
            int count = points.Count;
            float[] slopes = new float[count];

            for (int i = 0; i < count; i++)
            {
                if (i == 0) // 第一个节点
                {
                    slopes[i] = (points[i + 1].y - points[i].y) / (points[i + 1].x - points[i].x);
                }
                else if (i == count - 1) // 最后一个节点
                {
                    slopes[i] = (points[i].y - points[i - 1].y) / (points[i].x - points[i - 1].x);
                }
                else // 其余节点
                {
                    float dydx1 = (points[i].y - points[i - 1].y) / (points[i].x - points[i - 1].x);
                    float dydx2 = (points[i + 1].y - points[i].y) / (points[i + 1].x - points[i].x);
                    slopes[i] = (dydx1 + dydx2) / 2f;
                }
            }

            return slopes;
        }
    }
}

