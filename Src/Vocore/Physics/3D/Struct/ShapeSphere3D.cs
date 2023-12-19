using System;
using System.Numerics;
using System.Collections.Generic;



namespace Vocore
{
    public struct ShapeSphere3D : IShape3D
    {
        public Vector3 center;
        public float radius;

        public ShapeSphere3D(Vector3 center, float radius)
        {
            this.center = center;
            this.radius = radius;
        }

        public BoundingBox3D GetBoundingBox()
        {
            Vector3 extends = new Vector3(radius);
            return new BoundingBox3D(center - extends, center + extends);
        }

        public BoundingBox3D GetBoundingBox(Transform3D transform)
        {
            Vector3 centerInWorld = math.transform(transform, center);
            return new BoundingBox3D
            {
                min = centerInWorld - new Vector3(radius),
                max = centerInWorld + new Vector3(radius)
            };
        }
    }
}

