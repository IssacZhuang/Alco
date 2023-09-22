using System;

using Unity.Mathematics;

namespace Vocore
{
    public struct Ray
    {
        public float3 origin;
        public float3 displacement;

        public Ray(float3 origin, float3 displacement)
        {
            this.origin = origin;
            this.displacement = displacement;
        }

        public static Ray CreateWithStartAndEnd(float3 start, float3 end)
        {
            return new Ray
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