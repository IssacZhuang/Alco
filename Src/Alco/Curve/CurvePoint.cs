using System;
using System.Collections.Generic;

namespace Alco
{
    public struct CurvePoint<T> : IComparable<CurvePoint<T>>, ISortable
    {
        public float Time;
        public T Value;

        public CurvePoint(float t, T value)
        {
            this.Time = t;
            this.Value = value;
        }

        public float SortKey => this.Time;

        public int CompareTo(CurvePoint<T> other)
        {
            return this.Time.CompareTo(other.Time);
        }
    }
}

