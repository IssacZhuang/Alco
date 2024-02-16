using System;
using System.Numerics;

namespace Vocore
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
        public BoundingBox2D(Vector2 min, Vector2 max)
        {
            this.min = min;
            this.max = max;
        }

        public bool Intersects(BoundingBox2D other)
        {
            return min.X <= other.max.X && max.X >= other.min.X &&
                   min.Y <= other.max.Y && max.Y >= other.min.Y;
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

    }
}
