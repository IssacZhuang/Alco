using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    // math lib for 2d rotation
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Vector2 a, float radians)
        {
            sincos(radians, out float s, out float c);
            return new Vector2(a.X * c + a.Y * s, a.X * s + a.Y * c);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(float radians, Vector2 a)
        {
            sincos(radians, out float s, out float c);
            return new Vector2(a.X * c + a.Y * s, a.X * s + a.Y * c);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float direction(Vector2 a)
        {
            return atan2(a.Y, a.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 direction(float radians)
        {
            math.sincos(radians, out var sin, out var cos);
            return new Vector2(cos, sin);
        }
    }

}