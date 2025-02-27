using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    public struct ShapeSphere3D : IShape3D
    {
        public Vector3 Center;
        public float Radius;

        public ShapeSphere3D(Vector3 center, float radius)
        {
            this.Center = center;
            this.Radius = radius;
        }

        public BoundingBox3D GetBoundingBox()
        {
            Vector3 extends = new Vector3(Radius);
            return new BoundingBox3D(Center - extends, Center + extends);
        }

        public ShapeSphere3D TransformByParent(Transform3D parent)
        {
            return new ShapeSphere3D
            {
                Center = math.rotate(parent.Rotation, Center) * parent.Scale + parent.Position,
                Radius = Radius * math.max(parent.Scale.X, math.max(parent.Scale.Y, parent.Scale.Z))
            };
        }


        public override string ToString()
        {
            return $"Sphere: {Center}, {Radius}";
        }

    }
}

