using System;
using System.Numerics;

namespace Alco
{
    /// <summary>
    /// A 2D ray represented by an origin and a displacement vector.
    /// </summary>
    public struct Ray2D
    {
        public Vector2 Origin;
        public Vector2 Displacement;
        /// <summary>
        /// Initializes a ray with the given origin and displacement.
        /// </summary>
        /// <param name="origin">The start point of the ray.</param>
        /// <param name="displacement">The displacement from the origin to the ray end.</param>
        public Ray2D(Vector2 origin, Vector2 displacement)
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
        public static Ray2D CreateWithStartAndEnd(Vector2 start, Vector2 end)
        {
            return new Ray2D(start, end - start);
        }

        /// <summary>
        /// Gets the axis-aligned bounds of the ray segment.
        /// </summary>
        /// <returns>The bounding box spanning the origin and the ray end.</returns>
        public BoundingBox2D GetBoundingBox()
        {
            Vector2 end = Origin + Displacement;
            return new BoundingBox2D
            {
                Min = Vector2.Min(Origin, end),
                Max = Vector2.Max(Origin, end)
            };
        }

        public override string ToString()
        {
            return $"origin: {Origin}, displacement: {Displacement}";
        }

        // mutiply by scalar

        /// <summary>
        /// Scales the ray's displacement by the given scalar.
        /// </summary>
        /// <param name="ray">The ray to scale.</param>
        /// <param name="scalar">The displacement multiplier.</param>
        /// <returns>The scaled ray.</returns>
        public static Ray2D operator *(Ray2D ray, float scalar)
        {
            return new Ray2D(ray.Origin, ray.Displacement * scalar);
        }
    }
}