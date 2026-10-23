using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    /// <summary>
    /// An axis-aligned 2D bounding box defined by its minimum and maximum corners.
    /// </summary>
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

        /// <summary>
        /// Initializes a bounding box with the given minimum and maximum corners.
        /// </summary>
        /// <param name="min">The minimum corner.</param>
        /// <param name="max">The maximum corner.</param>
        public BoundingBox2D(Vector2 min, Vector2 max)
        {
            this.Min = min;
            this.Max = max;
        }

        /// <summary>
        /// Determines whether this box overlaps or touches another box.
        /// </summary>
        /// <param name="other">The other box to test.</param>
        /// <returns>True when the boxes overlap or touch.</returns>
        public bool Intersects(in BoundingBox2D other)
        {
            Vector2 minMax = Vector2.Max(Min, other.Min);
            Vector2 maxMin = Vector2.Min(Max, other.Max);
            Vector2 result = maxMin - minMax;
            return result.X >= 0 && result.Y >= 0;
            // return min.X <= other.max.X && max.X >= other.min.X &&
            //        min.Y <= other.max.Y && max.Y >= other.min.Y;
        }

        /// <summary>
        /// Determines whether the box contains the given point, boundaries included.
        /// </summary>
        /// <param name="point">The point to test.</param>
        /// <returns>True when the point lies inside or on the box.</returns>
        public bool Contains(in Vector2 point)
        {
            return Min.X <= point.X && Max.X >= point.X &&
                   Min.Y <= point.Y && Max.Y >= point.Y;
        }

        public override string ToString()
        {
            return $"Box: {Min} {Max}";
        }

        /// <summary>
        /// Gets the smallest box that contains both input boxes.
        /// </summary>
        /// <param name="a">The first box.</param>
        /// <param name="b">The second box.</param>
        /// <returns>The union of the two boxes.</returns>
        public static BoundingBox2D Merge(in BoundingBox2D a, in BoundingBox2D b)
        {
            return new BoundingBox2D
            {
                Min = math.min(a.Min, b.Min),
                Max = math.max(a.Max, b.Max),
            };
        }

        /// <summary>
        /// Gets the overlap of two boxes; when they do not overlap, the result has minimum
        /// values greater than its maximum values.
        /// </summary>
        /// <param name="a">The first box.</param>
        /// <param name="b">The second box.</param>
        /// <returns>The intersection of the two boxes.</returns>
        public static BoundingBox2D GetIntersection(in BoundingBox2D a, in BoundingBox2D b)
        {
            return new BoundingBox2D
            {
                Min = math.max(a.Min, b.Min),
                Max = math.min(a.Max, b.Max),
            };
        }

    }
}
