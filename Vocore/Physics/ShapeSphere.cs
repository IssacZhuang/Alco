using System;
using System.Collections.Generic;

using Unity.Mathematics;

namespace Vocore
{
    public struct ShapeSphere : IShape
    {
        public float3 center;
        public float radius;

        public ShapeSphere(float3 center, float radius)
        {
            this.center = center;
            this.radius = radius;
        }

        public BoundingBox GetBoundingBox()
        {
            float3 extends = new float3(radius, radius, radius);
            return new BoundingBox(center - extends, center + extends);
        }
    }
}

