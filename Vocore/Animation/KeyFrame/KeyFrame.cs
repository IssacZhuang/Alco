using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public struct KeyFrame<T>
    {
        public float t;
        public T value;

        public KeyFrame(float t, T value)
        {
            this.t = t;
            this.value = value;
        }
    }
}

