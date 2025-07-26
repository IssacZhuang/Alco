using System;
using System.Collections.Generic;



namespace Alco
{
    public class CurveLinear : ICurve
    {

        private readonly List<CurvePointValue> _points = new List<CurvePointValue>();
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

        public CurveLinear()
        {

        }

        public CurveLinear(float[] t, float[] value)
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
        }

        public CurveLinear(ReadOnlySpan<CurvePointValue> points)
        {
            _points.AddRange(points);
            Sort();
        }

        public CurveLinear(IReadOnlyList<CurvePointValue> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            _points.AddRange(points);
            Sort();
        }

        public CurvePointValue this[int i]
        {
            get
            {
                return _points[i];
            }
            set
            {
                _points[i] = value;
            }
        }

        public void SetPoints(ReadOnlySpan<CurvePointValue> points)
        {
            _points.Clear();
            _points.AddRange(points);
            Sort();
        }

        public void SetPoints(IReadOnlyList<CurvePointValue> points)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            _points.Clear();
            _points.AddRange(points);
            Sort();
        }

        public void Sort()
        {
            _points.Sort((CurvePointValue a, CurvePointValue b) => a.Time.CompareTo(b.Time));
        }
        

        public float Evaluate(float x)
        {
            if (_points.Count == 0)
            {
                return 0f;
            }
            if (x <= _points[0].Time)
            {
                return _points[0].Value;
            }
            if (x >= _points[_points.Count - 1].Time)
            {
                return _points[_points.Count - 1].Value;
            }
           
            int i = BinarySearchFloor(x);
            CurvePointValue keyFrame1 = _points[i];
            CurvePointValue keyFrame2 = _points[i + 1];
            float t = (x - keyFrame1.Time) / (keyFrame2.Time - keyFrame1.Time);
            return math.lerp(keyFrame1.Value, keyFrame2.Value, t);
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
    }
}

