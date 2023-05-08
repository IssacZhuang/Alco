using System;
using System.Runtime.CompilerServices;

using UnityEngine;

namespace Vocore
{
    public static class QuaternionEx
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 xzy(this Quaternion v)
        {
            return new Vector3(v.x, v.z, v.y);
        }

        public static Quaternion Invert(this Quaternion q)
        {
            q.x = -q.x;
            q.y = -q.y;
            q.z = -q.z;

            float lengthSq = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;


            float invLengthSq = 1.0f / lengthSq;
            q.x *= invLengthSq;
            q.y *= invLengthSq;
            q.z *= invLengthSq;
            q.w *= invLengthSq;

            return q;
        }
        
    }
}

