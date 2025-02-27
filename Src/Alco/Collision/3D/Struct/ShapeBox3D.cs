using System;
using System.Numerics;
using System.Collections.Generic;



namespace Alco
{
    public struct ShapeBox3D : IShape3D
    {
        public Vector3 center;
        public Vector3 extends;
        public Quaternion rotation;

        public ShapeBox3D(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.extends = size * 0.5f;
            this.rotation = Quaternion.Identity;
        }

        public ShapeBox3D(Vector3 center, Quaternion rotation, Vector3 size)
        {
            this.center = center;
            this.extends = size * 0.5f;
            this.rotation = rotation;
        }

        public ShapeBox3D(Vector3 center, Vector3 size, Quaternion rotation)
        {
            this.center = center;
            this.extends = size * 0.5f;
            this.rotation = rotation;
        }


        public BoundingBox3D GetBoundingBox()
        {
            if (rotation.Equals(Quaternion.Identity))
            {
                return new BoundingBox3D(center - extends, center + extends);
            }

            Vector3 x = math.rotate(rotation, new Vector3(extends.X, 0, 0));
            Vector3 y = math.rotate(rotation, new Vector3(0, extends.Y, 0));
            Vector3 z = math.rotate(rotation, new Vector3(0, 0, extends.Z));

            Vector3 extentsInB = math.abs(x) + math.abs(y) + math.abs(z);
            // Vector3 extentsInB = math.abs(math.rotate(extends, rotation));

            return new BoundingBox3D(center - extentsInB, center + extentsInB);
        }

        public ShapeBox3D TransformByParent(Transform3D parent)
        {
            return new ShapeBox3D
            {
                center = math.rotate(parent.Rotation, center) * parent.Scale + parent.Position,
                extends = extends * parent.Scale,
                rotation = math.mul(parent.Rotation, rotation),
            };
        }

        public override string ToString()
        {
            return $"Box: {center} {extends} {rotation}";
        }
    }
}

