using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public struct BoundingBox2D
    {
        /// <summary>
        /// The left bottom corner of the box
        /// </summary>
        public Vector2 min;
        /// <summary>
        /// The right top corner of the box
        /// </summary>
        public Vector2 max;


        public Vector2 Center
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (min + max) * 0.5f;
        }

        public Vector2 Size
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => max - min;
        }

        public BoundingBox2D(Vector2 min, Vector2 max)
        {
            this.min = min;
            this.max = max;
        }

        public bool Intersects(BoundingBox2D other)
        {
            Vector2 minMax = Vector2.Max(min, other.min);
            Vector2 maxMin = Vector2.Min(max, other.max);
            Vector2 result = maxMin - minMax;
            return result.X >= 0 && result.Y >= 0;
            // return min.X <= other.max.X && max.X >= other.min.X &&
            //        min.Y <= other.max.Y && max.Y >= other.min.Y;
        }

        public bool Contains(Vector2 point)
        {
            return min.X <= point.X && max.X >= point.X &&
                   min.Y <= point.Y && max.Y >= point.Y;
        }

        public override string ToString()
        {
            return $"Box: {min} {max}";
        }

        public static BoundingBox2D Merge(BoundingBox2D a, BoundingBox2D b)
        {
            return new BoundingBox2D
            {
                min = math.min(a.min, b.min),
                max = math.max(a.max, b.max),
            };
        }

        public static BoundingBox2D GetIntersection(BoundingBox2D a, BoundingBox2D b)
        {
            return new BoundingBox2D
            {
                min = math.max(a.min, b.min),
                max = math.min(a.max, b.max),
            };
        }

    }
}
