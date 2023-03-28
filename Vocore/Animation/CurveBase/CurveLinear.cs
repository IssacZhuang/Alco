using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public class CurveLinear:ICurve
    {

        private List<KeyFrame> _points = new List<KeyFrame>();
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

        public CurveLinear(float[] x, float[] y)
		{
			this._points = new List<KeyFrame>();
            for (int i = 0; i < x.Length; i++)
            {
                this._points.Add(new KeyFrame(x[i], y[i]));
            }
            this.SortPoints();
		}

		public CurveLinear(IEnumerable<KeyFrame> points)
		{
			this.SetPoints(points);
		}

		public CurveLinear()
		{
		}

		public KeyFrame this[int i]
		{
			get
			{
				return this._points[i];
			}
			set
			{
				this._points[i] = value;
			}
		}

		public void SetPoints(IEnumerable<KeyFrame> newPoints)
		{
			this._points.Clear();
			foreach (KeyFrame item in newPoints)
			{
				this._points.Add(item);
			}
			this.SortPoints();
		}

		public void Add(float x, float y, bool sort = true)
		{
			KeyFrame newPoint = new KeyFrame(x, y);
			this.Add(newPoint, sort);
		}

		public void Add(KeyFrame newPoint, bool sort = true)
		{
			this._points.Add(newPoint);
			if (sort)
			{
				this.SortPoints();
			}
		}

		public void SortPoints()
		{
			this._points.Sort((KeyFrame a, KeyFrame b) => a.t.CompareTo(b.t));
		}

		public float Evaluate(float x)
		{
			if (this._points.Count == 0)
			{
				return 0f;
			}
			if (x <= this._points[0].t)
			{
				return this._points[0].value;
			}
			if (x >= this._points[this._points.Count - 1].t)
			{
				return this._points[this._points.Count - 1].value;
			}
			KeyFrame keyFrame1 = this._points[0];
			KeyFrame keyFrame2 = this._points[this._points.Count - 1];
			int i = 0;
			while (i < this._points.Count)
			{
				if (x <= this._points[i].t)
				{
					keyFrame2 = this._points[i];
					if (i > 0)
					{
						keyFrame1 = this._points[i - 1];
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

