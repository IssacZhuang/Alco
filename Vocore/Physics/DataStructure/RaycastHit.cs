using System;
using System.Numerics;
using System.Collections.Generic;



namespace Vocore
{
    public struct RaycastHit
    {
        public Vector3 point;
        public Vector3 normal;
        public float fraction;

        public override string ToString()
        {
            return $"point: {point}, normal: {normal}";
        }
    }
}

