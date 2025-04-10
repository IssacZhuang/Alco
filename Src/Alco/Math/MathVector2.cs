using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 float2(float x, float y)
        {
            return new Vector2(x, y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 min(Vector2 a, Vector2 b)
        {
            return Vector2.Min(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 max(Vector2 a, Vector2 b)
        {
            return Vector2.Max(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 abs(Vector2 a)
        {
            return Vector2.Abs(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 select(Vector2 a, Vector2 b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 sign(Vector2 x)
        {
            return new Vector2(sign(x.X), sign(x.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 clamp(Vector2 a, Vector2 min, Vector2 max)
        {
            return Vector2.Clamp(a, min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 clamp(Vector2 a, float min, float max)
        {
            return Vector2.Clamp(a, new Vector2(min), new Vector2(max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 floor(Vector2 a)
        {
            return new Vector2(floor(a.X), floor(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ceil(Vector2 a)
        {
            return new Vector2(ceil(a.X), ceil(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 reciprocal(Vector2 a)
        {
            return Vector2.One / a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Vector2 a, Vector2 b)
        {
            return Vector2.Dot(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 pow(Vector2 a, float b)
        {
            return new Vector2(pow(a.X, b), pow(a.Y, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 pow(Vector2 a, Vector2 b)
        {
            return new Vector2(pow(a.X, b.X), pow(a.Y, b.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 normalize(Vector2 a)
        {
            return Vector2.Normalize(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 radians(Vector2 a)
        {
            return a * DegToRad;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 lerp(Vector2 a, Vector2 b, float t)
        {
            return Vector2.Lerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 acos(Vector2 a)
        {
            return new Vector2(acos(a.X), acos(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 asin(Vector2 a)
        {
            return new Vector2(asin(a.X), asin(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 atan(Vector2 a)
        {
            return new Vector2(atan(a.X), atan(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 atan2(Vector2 a, Vector2 b)
        {
            return new Vector2(atan2(a.X, b.X), atan2(a.Y, b.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 cos(Vector2 a)
        {
            return new Vector2(cos(a.X), cos(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 sin(Vector2 a)
        {
            return new Vector2(sin(a.X), sin(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 tan(Vector2 a)
        {
            return new Vector2(tan(a.X), tan(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 cosh(Vector2 a)
        {
            return new Vector2(cosh(a.X), cosh(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 sinh(Vector2 a)
        {
            return new Vector2(sinh(a.X), sinh(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 tanh(Vector2 a)
        {
            return new Vector2(tanh(a.X), tanh(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Vector2 a, out Vector2 s, out Vector2 c)
        {
            s = sin(a);
            c = cos(a);
        }

    }
}

