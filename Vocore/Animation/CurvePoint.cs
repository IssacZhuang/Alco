using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public struct CurvePoint<T>: ISortable
    {
        public float t;
        public T value;

        public CurvePoint(float t, T value)
        {
            this.t = t;
            this.value = value;
        }

        public float SortKey => t;
    }
}

