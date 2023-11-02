using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static partial class math
    {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform inverse(Transform a)
        {
            Quaternion invRot = inverse(a.rotation);
            return new Transform
            {
                position = mul(invRot, -a.position),
                rotation = invRot
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 transform(Transform a, Vector3 b)
        {
            return mul(a.rotation, b) + a.position;
        }




    }
}