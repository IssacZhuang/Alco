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
        public Vector2 Min;
        /// <summary>
        /// The right top corner of the box
        /// </summary>
        public Vector2 Max;


        public Vector2 Center
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Min + Max) * 0.5f;
        }

        public Vector2 Size
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Max - Min;
        }

        public BoundingBox2D(Vector2 min, Vector2 max)
        {
            this.Min = min;
            this.Max = max;
        }

        public bool Intersects(BoundingBox2D other)
        {
            Vector2 minMax = Vector2.Max(Min, other.Min);
            Vector2 maxMin = Vector2.Min(Max, other.Max);
            Vector2 result = maxMin - minMax;
            return result.X >= 0 && result.Y >= 0;
            // return min.X <= other.max.X && max.X >= other.min.X &&
            //        min.Y <= other.max.Y && max.Y >= other.min.Y;
        }

        public bool Contains(Vector2 point)
        {
            return Min.X <= point.X && Max.X >= point.X &&
                   Min.Y <= point.Y && Max.Y >= point.Y;
        }

        public override string ToString()
        {
            return $"Box: {Min} {Max}";
        }

        public static BoundingBox2D Merge(BoundingBox2D a, BoundingBox2D b)
        {
            return new BoundingBox2D
            {
                Min = math.min(a.Min, b.Min),
                Max = math.max(a.Max, b.Max),
            };
        }

        public static BoundingBox2D GetIntersection(BoundingBox2D a, BoundingBox2D b)
        {
            return new BoundingBox2D
            {
                Min = math.max(a.Min, b.Min),
                Max = math.min(a.Max, b.Max),
            };
        }

    }
}
