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

        public override string ToString()
        {
            return $"origin: {origin}, displacement: {displacement}";
        }
    }
}