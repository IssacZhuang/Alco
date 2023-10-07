using System;
using System.Collections.Generic;



namespace Vocore
{
    public struct BoundingBox
    {
        public float3 min;
        public float3 max;

        public BoundingBox(float3 min, float3 max)
        {
            this.min = min;
            this.max = max;
        }
        
        public bool Intersects(BoundingBox other)
        {
            return min.X <= other.max.X && max.X >= other.min.X &&
                   min.y <= other.max.y && max.y >= other.min.y &&
                   min.Z <= other.max.Z && max.Z >= other.min.Z;
        }
        
        public bool Contains(float3 point)
        {
            return min.X <= point.X && max.X >= point.X &&
                   min.y <= point.y && max.y >= point.y &&
                   min.Z <= point.Z && max.Z >= point.Z;
        }

        public override string ToString()
        {
            return $"Box: {min} {max}";
        }

        public static BoundingBox Merge(BoundingBox a, BoundingBox b)
        {
            return new BoundingBox
            {
                min = math.min(a.min, b.min),
                max = math.max(a.max, b.max),
            };
        }

    }
}