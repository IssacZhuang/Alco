using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    // math lib for 2d rotation
    public static partial class math
    {
        /// <summary>
        /// Linearly interpolates between two 2D rotations.
        /// </summary>
        /// <param name="a">The start rotation.</param>
        /// <param name="b">The end rotation.</param>
        /// <param name="t">The interpolation factor in the range [0, 1].</param>
        /// <returns>The interpolated rotation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D lerp(Rotation2D a, Rotation2D b, float t)
        {
            return Rotation2D.Lerp(a, b, t);
        }

        /// <summary>
        /// Spherically interpolates between two 2D rotations.
        /// </summary>
        /// <param name="a">The start rotation.</param>
        /// <param name="b">The end rotation.</param>
        /// <param name="t">The interpolation factor in the range [0, 1].</param>
        /// <returns>The interpolated rotation.</returns>
        // TODO: test
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D slerp(Rotation2D a, Rotation2D b, float t)
        {
            return Rotation2D.Slerp(a, b, t);
        }

        /// <summary>
        /// Returns the signed angle between two 2D rotations.
        /// </summary>
        /// <param name="a">The first rotation.</param>
        /// <param name="b">The second rotation.</param>
        /// <returns>The signed angle in radians.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float angle(Rotation2D a, Rotation2D b)
        {
            float dot = a.C * b.C + a.S * b.S;
            return acos(min(abs(dot), 1f)) * 2f * sign(dot);
        }

        /// <summary>
        /// Creates a 2D rotation from an angle in degrees.
        /// </summary>
        /// <param name="degree">The rotation angle in degrees.</param>
        /// <returns>The 2D rotation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D rotation2d(float degree)
        {
            return new Rotation2D(degree);
        }

        /// <summary>
        /// Rotates a vector by an angle in degrees.
        /// </summary>
        /// <param name="v">The vector to rotate.</param>
        /// <param name="degree">The rotation angle in degrees.</param>
        /// <returns>The rotated vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Vector2 v, float degree)
        {
            return rotate(new Rotation2D(degree), v);
        }

        /// <summary>
        /// Rotates a vector by an angle in degrees.
        /// </summary>
        /// <param name="degree">The rotation angle in degrees.</param>
        /// <param name="v">The vector to rotate.</param>
        /// <returns>The rotated vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(float degree, Vector2 v)
        {
            return rotate(new Rotation2D(degree), v);
        }

        /// <summary>
        /// Creates a 2D rotation facing a direction.
        /// </summary>
        /// <param name="a">The direction vector.</param>
        /// <returns>The 2D rotation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D direction(Vector2 a)
        {
            Vector2 norm = normalize(a);
            return new Rotation2D(-norm.Y, norm.X);
        }

        /// <summary>
        /// Returns the unit vector for an angle in radians.
        /// </summary>
        /// <param name="radians">The angle in radians.</param>
        /// <returns>The unit direction vector.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 direction(float radians)
        {
            sincos(radians, out var sin, out var cos);
            return new Vector2(cos, sin);
        }

        /// <summary>
        /// Rotates a vector by a 2D rotation using the left-handed convention.
        /// </summary>
        /// <param name="v">The vector to rotate.</param>
        /// <param name="q">The rotation.</param>
        /// <returns>The rotated vector.</returns>
        //left handed rotation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Vector2 v, Rotation2D q)
        {
            return new Vector2(q.C * v.X + q.S * v.Y, q.C * v.Y - q.S * v.X);
        }

        /// <summary>
        /// Rotates a vector by a 2D rotation using the left-handed convention.
        /// </summary>
        /// <param name="q">The rotation.</param>
        /// <param name="v">The vector to rotate.</param>
        /// <returns>The rotated vector.</returns>
        //left handed rotation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Rotation2D q, Vector2 v)
        {
            return new Vector2(q.C * v.X + q.S * v.Y, q.C * v.Y - q.S * v.X);
        }

        /// <summary>
        /// Rotates a vector by a 2D rotation using the left-handed convention.
        /// </summary>
        /// <param name="v">The vector to rotate.</param>
        /// <param name="q">The rotation.</param>
        /// <returns>The rotated vector.</returns>
        //left handed rotation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 mul(Vector2 v, Rotation2D q)
        {
            return new Vector2(q.C * v.X + q.S * v.Y, q.C * v.Y - q.S * v.X);
        }

        /// <summary>
        /// Rotates a vector by a 2D rotation using the left-handed convention.
        /// </summary>
        /// <param name="q">The rotation.</param>
        /// <param name="v">The vector to rotate.</param>
        /// <returns>The rotated vector.</returns>
        //left handed rotation
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 mul(Rotation2D q, Vector2 v)
        {
            return new Vector2(q.C * v.X + q.S * v.Y, q.C * v.Y - q.S * v.X);
        }

        /// <summary>
        /// Multiplies two 2D rotations.
        /// </summary>
        /// <param name="q">The first rotation.</param>
        /// <param name="r">The second rotation.</param>
        /// <returns>The combined rotation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D mul(Rotation2D q, Rotation2D r)
        {
            // Rotation2D qr;
            // qr.s = q.c * r.s + q.s * r.c; // Change the sign here
            // qr.c = q.c * r.c - q.s * r.s; // Change the sign here
            return new Rotation2D(q.C * r.S + q.S * r.C, q.C * r.C - q.S * r.S);
        }

        /// <summary>
        /// Returns the inverse of a 2D rotation.
        /// </summary>
        /// <param name="q">The rotation to invert.</param>
        /// <returns>The inverse rotation.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Rotation2D inverse(Rotation2D q)
        {
            return new Rotation2D(-q.S, q.C);
        }


        /// <summary>
        /// Returns the unit vector of a 2D rotation.
        /// </summary>
        /// <param name="q">The rotation.</param>
        /// <returns>The rotated +X unit vector.</returns>
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