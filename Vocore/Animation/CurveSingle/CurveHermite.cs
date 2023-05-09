using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public class CurveHermite : ICurve
    {

        private List<CurvePoint<float>> _points = new List<CurvePoint<float>>();
        private float[] _slopes;

        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IReadOnlyList<CurvePoint<float>> Points
        {
            get
            {
                return _points;
            }
        }

        public CurveHermite()
        {
        }

        public CurveHermite(IList<CurvePoint<float>> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }

            _points.AddRange(points);
          
            Sort();
            RefreshSlopes();
        }

        public CurveHermite(float[] t, float[] value)
        {
            if (t == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("t");
            }

            if (value == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("value");
            }

            if (t.Length != value.Length)
            {
                throw ExceptionCurve.UnequalPointsArray("t", "value");
            }

            for (int i = 0; i < t.Length; i++)
            {
                _points.Add(new CurvePoint<float>(t[i], value[i]));
            }
            Sort();
            RefreshSlopes();
        }

        public void SetPoints(IList<CurvePoint<float>> points)
        {
            _points = new List<CurvePoint<float>>(points);
            Sort();
            RefreshSlopes();
        }

        public float Evaluate(float t)
        {
            if (t <= _points[0].t)
            {
                return _points[0].value;
            }
            if (t >= _points[_points.Count - 1].t)
            {
                return _points[_points.Count - 1].value;
            }
            int index = BinarySearchFloor(t);

            float t0 = _points[index].t;
            float t1 = _points[index + 1].t;
            float p0 = _points[index].value;
            float p1 = _points[index + 1].value;
            float m0 = _slopes[index];
            float m1 = _slopes[index + 1];
            float dt = t1 - t0;
            float tNormalized = (t - t0) / dt;

            // Cubic Hermite Spline
            float h00 = 2 * math.pow(tNormalized, 3) - 3 * math.pow(tNormalized, 2) + 1;
            float h10 = math.pow(tNormalized, 3) - 2 * math.pow(tNormalized, 2) + tNormalized;
            float h01 = -2 * math.pow(tNormalized, 3) + 3 * math.pow(tNormalized, 2);
            float h11 = math.pow(tNormalized, 3) - math.pow(tNormalized, 2);

            float interpolatedValue = h00 * p0 + h10 * dt * m0 + h01 * p1 + h11 * dt * m1;
            return interpolatedValue;
        }

        public void Sort()
        {
            _points.Sort((a, b) => a.t.CompareTo(b.t));
        }

        public void RefreshSlopes()
        {
            _slopes = CalculateSlopes(_points);
        }

        private int BinarySearchFloor(float t){
            int low = 0;
            int high = _points.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (t < _points[mid].t)
                {
                    high = mid - 1;
                }
                else if (t > _points[mid].t)
                {
                    low = mid + 1;
                }
                else
                {
                    return mid;
                }
            }
            return high;
        }

        private static float[] CalculateSlopes(IList<CurvePoint<float>> points)
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

