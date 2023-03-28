using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public struct KeyFrame
    {
        public float t;
        public float value;

        public KeyFrame(float t, float value)
        {
            this.t = t;
            this.value = value;
        }
    }

    public struct KeyFrame2D
    {
        public float t;
        public Vector2 value;

        public KeyFrame2D(float t, Vector2 value)
        {
            this.t = t;
            this.value = value;
        }

        public KeyFrame2D(float t, float x, float y)
        {
            this.t = t;
            this.value = new Vector2(x, y);
        }
    }

    public struct KeyFrame3D
    {
        public float t;
        public Vector3 value;

        public KeyFrame3D(float t, Vector3 value)
        {
            this.t = t;
            this.value = value;
        }

        public KeyFrame3D(float t, float x, float y, float z)
        {
            this.t = t;
            this.value = new Vector3(x, y, z);
        }
    }

    public struct KeyFrame4D
    {
        public float t;
        public Vector4 value;

        public KeyFrame4D(float t, Vector4 value)
        {
            this.t = t;
            this.value = value;
        }

        public KeyFrame4D(float t, float x, float y, float z, float w)
        {
            this.t = t;
            this.value = new Vector4(x, y, z, w);
        }
    }
}

