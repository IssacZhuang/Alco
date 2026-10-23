using System;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Alco
{
    /// <summary>
    /// A 3D ray represented by an origin and a displacement vector.
    /// </summary>
    public struct Ray3D
    {
        public Vector3 Origin;
        public Vector3 Displacement;

        /// <summary>
        /// Initializes a ray with the given origin and displacement.
        /// </summary>
        /// <param name="origin">The start point of the ray.</param>
        /// <param name="displacement">The displacement from the origin to the ray end.</param>
        public Ray3D(Vector3 origin, Vector3 displacement)
        {
            this.Origin = origin;
            this.Displacement = displacement;
        }

        /// <summary>
        /// Creates a ray from a start point to an end point.
        /// </summary>
        /// <param name="start">The start point.</param>
        /// <param name="end">The end point.</param>
        /// <returns>The ray spanning the two points.</returns>
        public static Ray3D CreateWithStartAndEnd(Vector3 start, Vector3 end)
        {
            return new Ray3D
            {
                Origin = start,
                Displacement = end - start
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        /// <summary>
        /// Gets the axis-aligned bounds of the ray segment.
        /// </summary>
        /// <returns>The bounding box spanning the origin and the ray end.</returns>
        public BoundingBox3D GetBoundingBox()
        {
            Vector3 end = Origin + Displacement;
            return new BoundingBox3D
            {
                Min = Vector3.Min(Origin, end),
                Max = Vector3.Max(Origin, end)
            };
        }

        // mutiply by scalar

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        /// <summary>
        /// Scales the ray's displacement by the given scalar.
        /// </summary>
        /// <param name="ray">The ray to scale.</param>
        /// <param name="scalar">The displacement multiplier.</param>
        /// <returns>The scaled ray.</returns>
        public static Ray3D operator *(Ray3D ray, float scalar)
        {
            return new Ray3D(ray.Origin, ray.Displacement * scalar);
        }

        public override string ToString()
        {
            return $"origin: {Origin}, displacement: {Displacement}";
        }
    }
}