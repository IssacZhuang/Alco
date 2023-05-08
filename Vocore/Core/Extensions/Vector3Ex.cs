using System;
using System.Runtime.CompilerServices;

using UnityEngine;

namespace Vocore
{
    public static class Vector3Ex
    {
        #region Conversion
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 xz(this Vector3 v)
        {
            return new Vector2(v.x, v.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 xy(this Vector3 v)
        {
            return new Vector2(v.x, v.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 yz(this Vector3 v)
        {
            return new Vector2(v.y, v.z);
        }

        #endregion

        #region Calc

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Cross(this Vector3 a, Vector3 b)
        {
            return new Vector3(a.y * b.z - a.z * b.y,
                               a.z * b.x - a.x * b.z,
                               a.x * b.y - a.y * b.x);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Dot(this Vector3 a, Vector3 b)
        {
            return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Rotate(this Vector3 v, Quaternion q)
        {
            Vector3 t = 2 * Vector3.Cross(q.xzy(), v);
            return v + q.w * t + Vector3.Cross(q.xzy(), t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Abs(this Vector3 v)
        {
            return new Vector3(Math.Abs(v.x), Math.Abs(v.y), Math.Abs(v.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrMagnitude(this Vector3 v)
        {
            return v.x * v.x + v.y * v.y + v.z * v.z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrMagnitude(this Vector3 v, Vector3 other)
        {
            return (v - other).SqrMagnitude();
        }

    }

    #endregion
}

