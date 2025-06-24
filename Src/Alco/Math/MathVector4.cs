using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(float v)
        {
            return new Vector4(v, v, v, v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(float x, float y, float z, float w)
        {
            return new Vector4(x, y, z, w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(Vector2 xy, float z, float w)
        {
            return new Vector4(xy.X, xy.Y, z, w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(float x, Vector2 yz, float w)
        {
            return new Vector4(x, yz.X, yz.Y, w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(float x, float y, Vector2 zw)
        {
            return new Vector4(x, y, zw.X, zw.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(Vector3 xyz, float w)
        {
            return new Vector4(xyz.X, xyz.Y, xyz.Z, w);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 float4(float x, Vector3 yzw)
        {
            return new Vector4(x, yzw.X, yzw.Y, yzw.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 min(Vector4 a, Vector4 b)
        {
            return Vector4.Min(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 max(Vector4 a, Vector4 b)
        {
            return Vector4.Max(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 abs(Vector4 a)
        {
            return Vector4.Abs(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 select(Vector4 a, Vector4 b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 sign(Vector4 x)
        {
            return new Vector4(sign(x.X), sign(x.Y), sign(x.Z), sign(x.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 clamp(Vector4 a, Vector4 min, Vector4 max)
        {
            return Vector4.Clamp(a, min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 clamp(Vector4 a, float min, float max)
        {
            return Vector4.Clamp(a, new Vector4(min), new Vector4(max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 floor(Vector4 a)
        {
            return new Vector4(floor(a.X), floor(a.Y), floor(a.Z), floor(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 ceil(Vector4 a)
        {
            return new Vector4(ceil(a.X), ceil(a.Y), ceil(a.Z), ceil(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 reciprocal(Vector4 a)
        {
            return Vector4.One / a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Vector4 a, Vector4 b)
        {
            return Vector4.Dot(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 pow(Vector4 a, float b)
        {
            return new Vector4(pow(a.X, b), pow(a.Y, b), pow(a.Z, b), pow(a.W, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 pow(Vector4 a, Vector4 b)
        {
            return new Vector4(pow(a.X, b.X), pow(a.Y, b.Y), pow(a.Z, b.Z), pow(a.W, b.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 normalize(Vector4 a)
        {
            return Vector4.Normalize(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 radians(Vector4 a)
        {
            return a * DegToRad;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 lerp(Vector4 a, Vector4 b, float t)
        {
            return Vector4.Lerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 acos(Vector4 a)
        {
            return new Vector4(acos(a.X), acos(a.Y), acos(a.Z), acos(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 asin(Vector4 a)
        {
            return new Vector4(asin(a.X), asin(a.Y), asin(a.Z), asin(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 atan(Vector4 a)
        {
            return new Vector4(atan(a.X), atan(a.Y), atan(a.Z), atan(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 atan2(Vector4 a, Vector4 b)
        {
            return new Vector4(atan2(a.X, b.X), atan2(a.Y, b.Y), atan2(a.Z, b.Z), atan2(a.W, b.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 cos(Vector4 a)
        {
            return new Vector4(cos(a.X), cos(a.Y), cos(a.Z), cos(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 sin(Vector4 a)
        {
            return new Vector4(sin(a.X), sin(a.Y), sin(a.Z), sin(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 tan(Vector4 a)
        {
            return new Vector4(tan(a.X), tan(a.Y), tan(a.Z), tan(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 cosh(Vector4 a)
        {
            return new Vector4(cosh(a.X), cosh(a.Y), cosh(a.Z), cosh(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 sinh(Vector4 a)
        {
            return new Vector4(sinh(a.X), sinh(a.Y), sinh(a.Z), sinh(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 tanh(Vector4 a)
        {
            return new Vector4(tanh(a.X), tanh(a.Y), tanh(a.Z), tanh(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Vector4 a, out Vector4 s, out Vector4 c)
        {
            s = sin(a);
            c = cos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 round(Vector4 a)
        {
            return new Vector4(round(a.X), round(a.Y), round(a.Z), round(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 snap(Vector4 value, Vector4 interval, Vector4 offset)
        {
            return new Vector4(
                snap(value.X, interval.X, offset.X),
                snap(value.Y, interval.Y, offset.Y),
                snap(value.Z, interval.Z, offset.Z),
                snap(value.W, interval.W, offset.W)
            );
        }
    }
}

