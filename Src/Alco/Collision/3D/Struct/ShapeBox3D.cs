using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    public struct ShapeBox3D : IShape3D
    {
        public Vector3 Center;
        public Vector3 Extends;
        public Quaternion Rotation;

        public ShapeBox3D(Vector3 center, Vector3 size)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = Quaternion.Identity;
        }

        public ShapeBox3D(Vector3 center, Quaternion rotation, Vector3 size)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = rotation;
        }

        public ShapeBox3D(Vector3 center, Vector3 size, Quaternion rotation)
        {
            this.Center = center;
            this.Extends = size * 0.5f;
            this.Rotation = rotation;
        }


        public BoundingBox3D GetBoundingBox()
        {
            if (Rotation.Equals(Quaternion.Identity))
            {
                return new BoundingBox3D(Center - Extends, Center + Extends);
            }

            Vector3 x = math.rotate(Rotation, new Vector3(Extends.X, 0, 0));
            Vector3 y = math.rotate(Rotation, new Vector3(0, Extends.Y, 0));
            Vector3 z = math.rotate(Rotation, new Vector3(0, 0, Extends.Z));

            Vector3 extentsInB = math.abs(x) + math.abs(y) + math.abs(z);
            // Vector3 extentsInB = math.abs(math.rotate(extends, rotation));

            return new BoundingBox3D(Center - extentsInB, Center + extentsInB);
        }

        public ShapeBox3D TransformByParent(Transform3D parent)
        {
            return new ShapeBox3D
            {
                Center = math.rotate(parent.Rotation, Center) * parent.Scale + parent.Position,
                Extends = Extends * parent.Scale,
                Rotation = math.mul(parent.Rotation, Rotation),
            };
        }

        public override string ToString()
        {
            return $"Box: {Center} {Extends} {Rotation}";
        }
    }
}

