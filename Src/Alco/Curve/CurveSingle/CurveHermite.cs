using System;
using System.Collections.Generic;



namespace Alco
{
    public class CurveHermite : ICurve
    {

        private readonly List<CurvePointValue> _points = new List<CurvePointValue>();
        private readonly List<float> _slopes = new List<float>();

        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IReadOnlyList<CurvePointValue> Points
        {
            get
            {
                return _points;
            }
        }

        public CurveHermite()
        {

        }

        public CurveHermite(ReadOnlySpan<CurvePointValue> points)
        {
            _points.AddRange(points);
          
            Sort();
            CalculateSlopes(_slopes, _points);
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
                _points.Add(new CurvePointValue(t[i], value[i]));
            }
            Sort();
            CalculateSlopes(_slopes, _points);
        }

        public void SetPoints(ReadOnlySpan<CurvePointValue> points)
        {
            _points.Clear();
            _points.AddRange(points);
            Sort();
            CalculateSlopes(_slopes, _points);
        }

        public float Evaluate(float t)
        {
            if (t <= _points[0].Time)
            {
                return _points[0].Value;
            }
            if (t >= _points[_points.Count - 1].Time)
            {
                return _points[_points.Count - 1].Value;
            }
            int index = BinarySearchFloor(t);

            float t0 = _points[index].Time;
            float t1 = _points[index + 1].Time;
            float p0 = _points[index].Value;
            float p1 = _points[index + 1].Value;
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
            _points.Sort((a, b) => a.Time.CompareTo(b.Time));
        }


        private int BinarySearchFloor(float t){
            int low = 0;
            int high = _points.Count - 1;
            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (t < _points[mid].Time)
                {
                    high = mid - 1;
                }
                else if (t > _points[mid].Time)
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

        private static void CalculateSlopes(List<float> slopes, IReadOnlyList<CurvePointValue> points)
        {
            slopes.Clear();
            int count = points.Count;

            for (int i = 0; i < count; i++)
            {
                float slope;
                if (i == 0)
                {
                    slope = (points[i + 1].Value - points[i].Value) / (points[i + 1].Time - points[i].Time);
                }
                else if (i == count - 1)
                {
                    slope = (points[i].Value - points[i - 1].Value) / (points[i].Time - points[i - 1].Time);
                }
                else
                {
                    float dydx1 = (points[i].Value - points[i - 1].Value) / (points[i].Time - points[i - 1].Time);
                    float dydx2 = (points[i + 1].Value - points[i].Value) / (points[i + 1].Time - points[i].Time);
                    slope = (dydx1 + dydx2) / 2f;
                }

                slopes.Add(slope);
            }
        }
    }
}

