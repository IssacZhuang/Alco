using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore.Animation
{
    public class CurveLinear:ICurve
    {

        private List<Vector2> _points = new List<Vector2>();
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

        public CurveLinear(float[] x, float[] y)
		{
			this._points = new List<Vector2>();
            for (int i = 0; i < x.Length; i++)
            {
                this._points.Add(new Vector2(x[i], y[i]));
            }
            this.SortPoints();
		}

		public CurveLinear(IEnumerable<Vector2> points)
		{
			this.SetPoints(points);
		}

		public CurveLinear()
		{
		}

		public Vector2 this[int i]
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

		public void SetPoints(IEnumerable<Vector2> newPoints)
		{
			this._points.Clear();
			foreach (Vector2 item in newPoints)
			{
				this._points.Add(item);
			}
			this.SortPoints();
		}

		public void Add(float x, float y, bool sort = true)
		{
			Vector2 newPoint = new Vector2(x, y);
			this.Add(newPoint, sort);
		}

		public void Add(Vector2 newPoint, bool sort = true)
		{
			this._points.Add(newPoint);
			if (sort)
			{
				this.SortPoints();
			}
		}

		public void SortPoints()
		{
			this._points.Sort(CurveLinear.Vector2sComparer);
		}

		public float Evaluate(float x)
		{
			if (this._points.Count == 0)
			{
				return 0f;
			}
			if (x <= this._points[0].x)
			{
				return this._points[0].y;
			}
			if (x >= this._points[this._points.Count - 1].x)
			{
				return this._points[this._points.Count - 1].y;
			}
			Vector2 Vector2 = this._points[0];
			Vector2 Vector22 = this._points[this._points.Count - 1];
			int i = 0;
			while (i < this._points.Count)
			{
				if (x <= this._points[i].x)
				{
					Vector22 = this._points[i];
					if (i > 0)
					{
						Vector2 = this._points[i - 1];
						break;
					}
					break;
				}
				else
				{
					i++;
				}
			}
			float t = (x - Vector2.x) / (Vector22.x - Vector2.x);
			return Mathf.Lerp(Vector2.y, Vector22.y, t);
		}

		private static Comparison<Vector2> Vector2sComparer = delegate(Vector2 a, Vector2 b)
		{
			if (a.x < b.x)
			{
				return -1;
			}
			if (b.x < a.x)
			{
				return 1;
			}
			return 0;
		};
    }
}

