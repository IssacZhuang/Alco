using System;
using System.Numerics;
using System.Collections.Generic;



namespace Vocore
{
    public struct ShapeBox3D : IShape3D
    {
        public Vector3 center;
        public Vector3 extends;
        public Quaternion rotation;

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

            Vector3 halfExtentsInB = math.abs(x) + math.abs(y) + math.abs(z);

            return new BoundingBox3D(center - halfExtentsInB, center + halfExtentsInB);
        }

        public BoundingBox3D GetBoundingBox(Transform transform)
        {
            // Vector3 centerInWorld = math.transform(transform, center);
            // Quaternion rotationInWorld = math.mul(transform.rot, rotation);

            // if (rotationInWorld.Equals(Quaternion.Identity))
            // {
            //     return new BoundingBox(centerInWorld - extends, centerInWorld + extends);
            // }

            // Vector3 x = math.rotate(rotationInWorld, new Vector3(extends.X, 0, 0));
            // Vector3 y = math.rotate(rotationInWorld, new Vector3(0, extends.Y, 0));
            // Vector3 z = math.rotate(rotationInWorld, new Vector3(0, 0, extends.Z));

            // Vector3 halfExtentsInB = math.abs(x) + math.abs(y) + math.abs(z);

            // return new BoundingBox(centerInWorld - halfExtentsInB, centerInWorld + halfExtentsInB);
            Quaternion rotationInWorld = math.mul(rotation, transform.rotation);

            Vector3 x = Vector3.Transform(new Vector3(extends.X, 0, 0), rotationInWorld);
            Vector3 y = Vector3.Transform(new Vector3(0, extends.Y, 0), rotationInWorld);
            Vector3 z = Vector3.Transform(new Vector3(0, 0, extends.Z), rotationInWorld);

            Vector3 halfExtentsInB = math.abs(x) + math.abs(y) + math.abs(z);

            Vector3 centerInWorld = math.transform(transform, center);

            return new BoundingBox3D(centerInWorld - halfExtentsInB, centerInWorld + halfExtentsInB);
        }

        public override string ToString()
        {
            return $"Box: {center} {extends} {rotation}";
        }
    }
}

