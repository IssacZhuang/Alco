using System;
using System.Collections.Generic;
using System.Numerics;


namespace Vocore
{
    public struct BoundingBox3D
    {
        public Vector3 min;
        public Vector3 max;

        public BoundingBox3D(Vector3 min, Vector3 max)
        {
            this.min = min;
            this.max = max;
        }

        public bool Intersects(BoundingBox3D other)
        {
            return min.X <= other.max.X && max.X >= other.min.X &&
                   min.Y <= other.max.Y && max.Y >= other.min.Y &&
                   min.Z <= other.max.Z && max.Z >= other.min.Z;
        }

        public bool Contains(Vector3 point)
        {
            return min.X <= point.X && max.X >= point.X &&
                   min.Y <= point.Y && max.Y >= point.Y &&
                   min.Z <= point.Z && max.Z >= point.Z;
        }

        public override string ToString()
        {
            return $"Box: {min} {max}";
        }

        public static BoundingBox3D Merge(BoundingBox3D a, BoundingBox3D b)
        {
            return new BoundingBox3D
            {
                min = math.min(a.min, b.min),
                max = math.max(a.max, b.max),
            };
        }

    }
}