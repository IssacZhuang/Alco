using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public class CurveLinear : ICurve
    {

        private List<KeyFrame<float>> _points = new List<KeyFrame<float>>();
        public int PointsCount
        {
            get
            {
                return _points.Count;
            }
        }

        public IList<KeyFrame<float>> Points
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
                _points.Add(new KeyFrame<float>(t[i], value[i]));
            }
            Sort();
        }

        public CurveLinear(IList<KeyFrame<float>> points)
        {
            if (points == null)
            {
                throw ExceptionCurve.NullOrEmptyPoints("points");
            }
            _points = new List<KeyFrame<float>>(points);
            Sort();
        }

        public KeyFrame<float> this[int i]
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

        public void SetPoints(IList<KeyFrame<float>> points)
        {
            _points = new List<KeyFrame<float>>(points);
            Sort();
        }

        public void Sort()
        {
            _points.Sort((KeyFrame<float> a, KeyFrame<float> b) => a.t.CompareTo(b.t));
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
            KeyFrame<float> keyFrame1 = _points[0];
            KeyFrame<float> keyFrame2 = _points[_points.Count - 1];
            int i = 0;
            while (i < _points.Count)
            {
                if (x <= _points[i].t)
                {
                    keyFrame2 = _points[i];
                    if (i > 0)
                    {
                        keyFrame1 = _points[i - 1];
                        break;
                    }
                    break;
                }
                else
                {
                    i++;
                }
            }
            float t = (x - keyFrame1.t) / (keyFrame2.t - keyFrame1.t);
            return Mathf.Lerp(keyFrame1.value, keyFrame2.value, t);
        }
    }
}

