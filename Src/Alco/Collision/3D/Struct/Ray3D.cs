using System;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Alco
{
    public struct Ray3D
    {
        public Vector3 Origin;
        public Vector3 Displacement;

        public Ray3D(Vector3 origin, Vector3 displacement)
        {
            this.Origin = origin;
            this.Displacement = displacement;
        }

        public static Ray3D CreateWithStartAndEnd(Vector3 start, Vector3 end)
        {
            return new Ray3D
            {
                Origin = start,
                Displacement = end - start
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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