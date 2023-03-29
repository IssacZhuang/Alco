using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public struct CurvePoint<T>
    {
        public float t;
        public T value;

        public CurvePoint(float t, T value)
        {
            this.t = t;
            this.value = value;
        }
    }
}

