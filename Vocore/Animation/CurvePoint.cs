using System;
using System.Collections.Generic;

namespace Vocore
{
    public struct CurvePoint<T> : IComparable<CurvePoint<T>>, ISortable
    {
        public float t;
        public T value;

        public CurvePoint(float t, T value)
        {
            this.t = t;
            this.value = value;
        }

        public float SortKey => t;

        public int CompareTo(CurvePoint<T> other)
        {
            return t.CompareTo(other.t);
        }
    }
}

