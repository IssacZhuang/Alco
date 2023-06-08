using System;
using System.Collections.Generic;

using Unity.Mathematics;

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
            return min.x <= other.max.x && max.x >= other.min.x &&
                   min.y <= other.max.y && max.y >= other.min.y &&
                   min.z <= other.max.z && max.z >= other.min.z;
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