using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Alco
{
    public struct BoundingBox3D
    {
        /// <summary>
        /// The left bottom corner of the box
        /// </summary>
        public Vector3 Min;
        /// <summary>
        /// The right top corner of the box
        /// </summary>
        public Vector3 Max;

        public Vector3 Center
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (Min + Max) * 0.5f;
        }

        public Vector3 Size
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Max - Min;
        }

        public BoundingBox3D(Vector3 min, Vector3 max)
        {
            this.Min = min;
            this.Max = max;
        }

        public bool Intersects(in BoundingBox3D other)
        {
            Vector3 minMax = Vector3.Max(Min, other.Min);
            Vector3 maxMin = Vector3.Min(Max, other.Max);
            Vector3 result = maxMin - minMax;
            return result.X >= 0 && result.Y >= 0 && result.Z >= 0;
            // return min.X <= other.max.X && max.X >= other.min.X &&
            //        min.Y <= other.max.Y && max.Y >= other.min.Y &&
            //        min.Z <= other.max.Z && max.Z >= other.min.Z;
        }

        public bool Contains(Vector3 point)
        {
            return Min.X <= point.X && Max.X >= point.X &&
                   Min.Y <= point.Y && Max.Y >= point.Y &&
                   Min.Z <= point.Z && Max.Z >= point.Z;
        }

        public override string ToString()
        {
            return $"Box: {Min} {Max}";
        }

        public static BoundingBox3D Merge(in BoundingBox3D a, in BoundingBox3D b)
        {
            return new BoundingBox3D
            {
                Min = math.min(a.Min, b.Min),
                Max = math.max(a.Max, b.Max),
            };
        }

        public static BoundingBox3D GetIntersection(in BoundingBox3D a, in BoundingBox3D b)
        {
            return new BoundingBox3D
            {
                Min = math.max(a.Min, b.Min),
                Max = math.min(a.Max, b.Max),
            };
        }

        /// <summary>
        /// Transforms the box and returns the axis-aligned bounds of the result.
        /// </summary>
        public BoundingBox3D Transform(in Matrix4x4 transform)
        {
            Vector3 center = Center;
            Vector3 extents = Size * 0.5f;
            Vector3 transformedCenter = Vector3.Transform(center, transform);
            Vector3 transformedExtents = new(
                MathF.Abs(transform.M11) * extents.X + MathF.Abs(transform.M21) * extents.Y + MathF.Abs(transform.M31) * extents.Z,
                MathF.Abs(transform.M12) * extents.X + MathF.Abs(transform.M22) * extents.Y + MathF.Abs(transform.M32) * extents.Z,
                MathF.Abs(transform.M13) * extents.X + MathF.Abs(transform.M23) * extents.Y + MathF.Abs(transform.M33) * extents.Z);
            return new BoundingBox3D(transformedCenter - transformedExtents, transformedCenter + transformedExtents);
        }

    }
}