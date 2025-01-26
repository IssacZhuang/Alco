using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
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

        public ShapeSphere3D TransformByParent(Transform3D parent)
        {
            return new ShapeSphere3D
            {
                center = math.rotate(parent.rotation, center) * parent.scale + parent.position,
                radius = radius * math.max(parent.scale.X, math.max(parent.scale.Y, parent.scale.Z))
            };
        }


        public override string ToString()
        {
            return $"Sphere: {center}, {radius}";
        }

    }
}

