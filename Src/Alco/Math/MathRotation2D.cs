using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    // math lib for 2d rotation
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D lerp(Rotation2D a, Rotation2D b, float t)
        {
            return Rotation2D.Lerp(a, b, t);
        }

        // TODO: test
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D slerp(Rotation2D a, Rotation2D b, float t)
        {
            return Rotation2D.Slerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float angle(Rotation2D a, Rotation2D b)
        {
            float dot = a.C * b.C + a.S * b.S;
            return acos(min(abs(dot), 1f)) * 2f * sign(dot);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D rotation2d(float degree)
        {
            return new Rotation2D(degree);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Vector2 v, float degree)
        {
            return rotate(new Rotation2D(degree), v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(float degree, Vector2 v)
        {
            return rotate(new Rotation2D(degree), v);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D direction(Vector2 a)
        {
            Vector2 norm = normalize(a);
            return new Rotation2D(-norm.Y, norm.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 direction(float radians)
        {
            sincos(radians, out var sin, out var cos);
            return new Vector2(cos, sin);
        }

        //left handed rotation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Vector2 v, Rotation2D q)
        {
            return new Vector2(q.C * v.X + q.S * v.Y, q.C * v.Y - q.S * v.X);
        }

        //left handed rotation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Rotation2D q, Vector2 v)
        {
            return new Vector2(q.C * v.X + q.S * v.Y, q.C * v.Y - q.S * v.X);
        }

        //left handed rotation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 mul(Vector2 v, Rotation2D q)
        {
            return new Vector2(q.C * v.X + q.S * v.Y, q.C * v.Y - q.S * v.X);
        }

        //left handed rotation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 mul(Rotation2D q, Vector2 v)
        {
            return new Vector2(q.C * v.X + q.S * v.Y, q.C * v.Y - q.S * v.X);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D mul(Rotation2D q, Rotation2D r)
        {
            // Rotation2D qr;
            // qr.s = q.c * r.s + q.s * r.c; // Change the sign here
            // qr.c = q.c * r.c - q.s * r.s; // Change the sign here
            return new Rotation2D(q.C * r.S + q.S * r.C, q.C * r.C - q.S * r.S);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D inverse(Rotation2D q)
        {
            return new Rotation2D(-q.S, q.C);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 direction(Rotation2D q)
        {
            return new Vector2(q.C, q.S);
        }

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public static Rotation2D direction(Vector2 v)
        // {
        //     v = normalize(v);
        //     return new Rotation2D(v.X, v.Y);
        // }
    }

}