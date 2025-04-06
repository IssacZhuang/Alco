using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 float3(float v)
        {
            return new Vector3(v, v, v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 float3(float x, float y, float z)
        {
            return new Vector3(x, y, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 float3(Vector2 xy, float z)
        {
            return new Vector3(xy.X, xy.Y, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 float3(float x, Vector2 yz)
        {
            return new Vector3(x, yz.X, yz.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 min(Vector3 a, Vector3 b)
        {
            return Vector3.Min(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 max(Vector3 a, Vector3 b)
        {
            return Vector3.Max(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 abs(Vector3 a)
        {
            return Vector3.Abs(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 select(Vector3 a, Vector3 b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 sign(Vector3 x)
        {
            return new Vector3(sign(x.X), sign(x.Y), sign(x.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 clamp(Vector3 a, Vector3 min, Vector3 max)
        {
            return Vector3.Clamp(a, min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 clamp(Vector3 a, float min, float max)
        {
            return Vector3.Clamp(a, new Vector3(min), new Vector3(max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 floor(Vector3 a)
        {
            return new Vector3(floor(a.X), floor(a.Y), floor(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ceil(Vector3 a)
        {
            return new Vector3(ceil(a.X), ceil(a.Y), ceil(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 reciprocal(Vector3 a)
        {
            return Vector3.One / a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Vector3 a, Vector3 b)
        {
            return Vector3.Dot(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 pow(Vector3 a, float b)
        {
            return new Vector3(pow(a.X, b), pow(a.Y, b), pow(a.Z, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 pow(Vector3 a, Vector3 b)
        {
            return new Vector3(pow(a.X, b.X), pow(a.Y, b.Y), pow(a.Z, b.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 normalize(Vector3 a)
        {
            return Vector3.Normalize(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 radians(Vector3 degree)
        {
            return degree * DegToRad;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 degrees(Vector3 radian)
        {
            return radian * RadToDeg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 lerp(Vector3 a, Vector3 b, float t)
        {
            return Vector3.Lerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 acos(Vector3 a)
        {
            return new Vector3(acos(a.X), acos(a.Y), acos(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 asin(Vector3 a)
        {
            return new Vector3(asin(a.X), asin(a.Y), asin(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 atan(Vector3 a)
        {
            return new Vector3(atan(a.X), atan(a.Y), atan(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 atan2(Vector3 a, Vector3 b)
        {
            return new Vector3(atan2(a.X, b.X), atan2(a.Y, b.Y), atan2(a.Z, b.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 cos(Vector3 a)
        {
            //simd
            return Vector3.Cos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 sin(Vector3 a)
        {
            //simd
            return Vector3.Sin(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 tan(Vector3 a)
        {
            return new Vector3(tan(a.X), tan(a.Y), tan(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 cosh(Vector3 a)
        {
            return new Vector3(cosh(a.X), cosh(a.Y), cosh(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 sinh(Vector3 a)
        {
            return new Vector3(sinh(a.X), sinh(a.Y), sinh(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 tanh(Vector3 a)
        {
            return new Vector3(tanh(a.X), tanh(a.Y), tanh(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Vector3 a, out Vector3 s, out Vector3 c)
        {
            //simd
            (s, c) = Vector3.SinCos(a);
        }

    }
}

