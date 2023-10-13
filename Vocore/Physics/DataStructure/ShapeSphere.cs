using System;
using System.Numerics;
using System.Collections.Generic;



namespace Vocore
{
    public struct ShapeSphere : IShape
    {
        public Vector3 center;
        public float radius;

        public ShapeSphere(Vector3 center, float radius)
        {
            this.center = center;
            this.radius = radius;
        }

        public BoundingBox GetBoundingBox()
        {
            Vector3 extends = new Vector3(radius);
            return new BoundingBox(center - extends, center + extends);
        }

        public BoundingBox GetBoundingBox(Transform transform)
        {
            Vector3 centerInWorld = math.transform(transform, center);
            return new BoundingBox
            {
                min = centerInWorld - new Vector3(radius),
                max = centerInWorld + new Vector3(radius)
            };
        }
    }
}

