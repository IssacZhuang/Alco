using System;
using System.Numerics;


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

        public override string ToString()
        {
            return $"origin: {origin}, displacement: {displacement}";
        }
    }
}