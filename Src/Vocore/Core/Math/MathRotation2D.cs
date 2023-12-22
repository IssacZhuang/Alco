using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    // math lib for 2d rotation
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Vector2 v, float radians)
        {
            sincos(radians, out float s, out float c);
            return new Vector2(c * v.X - s * v.Y, s * v.X + c * v.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(float radians, Vector2 v)
        {
            sincos(radians, out float s, out float c);
            return new Vector2(c * v.X - s * v.Y, s * v.X + c * v.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float direction(Vector2 a)
        {
            return atan2(a.Y, a.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 direction(float radians)
        {
            sincos(radians, out var sin, out var cos);
            return new Vector2(cos, sin);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Vector2 v, Rotation2D q)
        {
            return new Vector2(q.c * v.X - q.s * v.Y, q.s * v.X + q.c * v.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Rotation2D q, Vector2 v)
        {

            return new Vector2(q.c * v.X - q.s * v.Y, q.s * v.X + q.c * v.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D mul(Rotation2D q, Rotation2D r)
        {
            // Rotation2D qr;
            // qr.s = q.c * r.s - q.s * r.c;
            // qr.c = q.c * r.c + q.s * r.s;
            return new Rotation2D(q.c * r.s - q.s * r.c, q.c * r.c + q.s * r.s);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D inverse(Rotation2D q)
        {
            return new Rotation2D(-q.s, q.c);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 direction(Rotation2D q)
        {
            return new Vector2(q.c, q.s);
        }
    }

}