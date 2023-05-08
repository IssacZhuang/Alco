using System;
using System.Collections.Generic;

using UnityEngine;

namespace Vocore
{
    public struct ShapeBox: IShape
    {
        public Vector3 center;
        public Vector3 extends;
        public Quaternion rotation;

        public ShapeBox(Vector3 center, Vector3 size, Quaternion rotation)
        {
            this.center = center;
            this.extends = size * 0.5f;
            this.rotation = rotation;
        }


        public BoundingBox GetBoundingBox()
        {
            if (rotation == Quaternion.identity)
            {
                return new BoundingBox(center - extends, center + extends);
            }

            Vector3 x = new Vector3(extends.x, 0, 0).Rotate(rotation);
            Vector3 y = new Vector3(0, extends.y, 0).Rotate(rotation);
            Vector3 z = new Vector3(0, 0, extends.z).Rotate(rotation);

            Vector3 halfExtentsInB = x.Abs() + y.Abs() + z.Abs();

            return new BoundingBox(center - halfExtentsInB, center + halfExtentsInB);
        }
    }
}

