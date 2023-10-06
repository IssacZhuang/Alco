using System;
using System.Collections.Generic;



namespace Vocore
{
    public struct RaycastHit
    {
        public float3 point;
        public float3 normal;
        public float fraction;

        public override string ToString()
        {
            return $"point: {point}, normal: {normal}";
        }
    }
}

