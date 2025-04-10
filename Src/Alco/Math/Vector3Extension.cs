using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static class Vector3Extension
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 YZX(this Vector3 v)
        {
            return new Vector3(v.Y, v.Z, v.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ZXY(this Vector3 v)
        {
            return new Vector3(v.Z, v.X, v.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 XZY(this Vector3 v)
        {
            return new Vector3(v.X, v.Z, v.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 YXZ(this Vector3 v)
        {
            return new Vector3(v.Y, v.X, v.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ZYX(this Vector3 v)
        {
            return new Vector3(v.Z, v.Y, v.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 XY(this Vector3 v)
        {
            return new Vector2(v.X, v.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 XZ(this Vector3 v)
        {
            return new Vector2(v.X, v.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 YZ(this Vector3 v)
        {
            return new Vector2(v.Y, v.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 YX(this Vector3 v)
        {
            return new Vector2(v.Y, v.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ZX(this Vector3 v)
        {
            return new Vector2(v.Z, v.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ZY(this Vector3 v)
        {
            return new Vector2(v.Z, v.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion DegreesToQuaternion(this Vector3 v)
        {
            return math.quaternion(v * math.DegToRad);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion RadiansToQuaternion(this Vector3 v)
        {
            return math.quaternion(v);
        }
    }
}