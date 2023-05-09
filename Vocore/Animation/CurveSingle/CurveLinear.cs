using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public class CurveLinear : ICurve
    {

        private List<CurvePoint<float>> _points = new List<CurvePoint<float>>();
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
                _points.Add(new CurvePoint<float>(t[i], value[i]));
            }
            Sort();
        }

        public CurveLinear(IList<CurvePoint<float>> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }
            _points.AddRange(points);
            Sort();
        }

        public CurvePoint<float> this[int i]
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

        public void SetPoints(IList<CurvePoint<float>> points)
        {
            _points = new List<CurvePoint<float>>(points);
            Sort();
        }

        public void Sort()
        {
            _points.Sort((CurvePoint<float> a, CurvePoint<float> b) => a.t.CompareTo(b.t));
        }
        

        public float Evaluate(float x)
        {
            if (_points.Count == 0)
            {
                return 0f;
            }
            if (x <= _points[0].t)
            {
                return _points[0].value;
            }
            if (x >= _points[_points.Count - 1].t)
            {
                return _points[_points.Count - 1].value;
            }
           
            int i = BinarySearchFloor(x);
            CurvePoint<float> keyFrame1 = _points[i];
            CurvePoint<float> keyFrame2 = _points[i + 1];
            float t = (x - keyFrame1.t) / (keyFrame2.t - keyFrame1.t);
            return math.lerp(keyFrame1.value, keyFrame2.value, t);
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
    }
}

