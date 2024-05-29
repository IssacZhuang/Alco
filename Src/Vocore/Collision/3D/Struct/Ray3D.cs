using System;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Vocore
{
    public struct Ray3D
    {
        public Vector3 origin;
        public Vector3 displacement;

        public Ray3D(Vector3 origin, Vector3 displacement)
        {
            this.origin = origin;
            this.displacement = displacement;
        }

        public static Ray3D CreateWithStartAndEnd(Vector3 start, Vector3 end)
        {
            return new Ray3D
            {
                origin = start,
                displacement = end - start
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BoundingBox3D GetBoundingBox()
        {
            Vector3 end = origin + displacement;
            return new BoundingBox3D
            {
                min = Vector3.Min(origin, end),
                max = Vector3.Max(origin, end)
            };
        }

        // mutiply by scalar

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Ray3D operator *(Ray3D ray, float scalar)
        {
            return new Ray3D(ray.origin, ray.displacement * scalar);
        }

        public override string ToString()
        {
            return $"origin: {origin}, displacement: {displacement}";
        }
    }
}