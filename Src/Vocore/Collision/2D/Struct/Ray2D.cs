using System;
using System.Numerics;

namespace Vocore
{
    public struct Ray2D
    {
        public Vector2 origin;
        public Vector2 displacement;
        public Ray2D(Vector2 origin, Vector2 displacement)
        {
            this.origin = origin;
            this.displacement = displacement;
        }

        public static Ray2D CreateWithStartAndEnd(Vector2 start, Vector2 end)
        {
            return new Ray2D(start, end - start);
        }

        public BoundingBox2D GetBoundingBox()
        {
            Vector2 end = origin + displacement;
            return new BoundingBox2D
            {
                min = Vector2.Min(origin, end),
                max = Vector2.Max(origin, end)
            };
        }

        public override string ToString()
        {
            return $"origin: {origin}, displacement: {displacement}";
        }

        // mutiply by scalar

        public static Ray2D operator *(Ray2D ray, float scalar)
        {
            return new Ray2D(ray.origin, ray.displacement * scalar);
        }
    }
}