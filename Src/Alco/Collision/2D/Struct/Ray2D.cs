using System;
using System.Numerics;

namespace Alco
{
    public struct Ray2D
    {
        public Vector2 Origin;
        public Vector2 Displacement;
        public Ray2D(Vector2 origin, Vector2 displacement)
        {
            this.Origin = origin;
            this.Displacement = displacement;
        }

        public static Ray2D CreateWithStartAndEnd(Vector2 start, Vector2 end)
        {
            return new Ray2D(start, end - start);
        }

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

        public static Ray2D operator *(Ray2D ray, float scalar)
        {
            return new Ray2D(ray.Origin, ray.Displacement * scalar);
        }
    }
}