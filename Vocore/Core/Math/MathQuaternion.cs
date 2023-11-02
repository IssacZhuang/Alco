using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion mul(Quaternion a, Quaternion b)
        {
            return Quaternion.Multiply(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion inverse(Quaternion a)
        {
            return Quaternion.Inverse(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion lerp(Quaternion a, Quaternion b, float t)
        {
            return Quaternion.Lerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Euler(Vector3 xyz)
        {
            return Quaternion.CreateFromYawPitchRoll(xyz.Y, xyz.X, xyz.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Euler(float x, float y, float z)
        {
            return Quaternion.CreateFromYawPitchRoll(y, x, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion EulerXYZ(Vector3 xyz)
        {
            return Quaternion.CreateFromYawPitchRoll(xyz.Y, xyz.X, xyz.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion EulerXYZ(float x, float y, float z)
        {
            return Quaternion.CreateFromYawPitchRoll(y, x, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion FromDirection(Vector3 dir)
        {
            return Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateLookAt(Vector3.Zero, dir, Vector3.UnitY));
        }

        //todo: use simd
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Quaternion a, Quaternion b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 mul(Quaternion a, Vector2 b)
        {
            return Vector2.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 mul(Vector2 a, Quaternion b)
        {
            return Vector2.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Vector2 a, Quaternion b)
        {
            return Vector2.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Quaternion a, Vector2 b)
        {
            return Vector2.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 mul(Vector3 a, Quaternion b)
        {
            return Vector3.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 mul(Quaternion a, Vector3 b)
        {
            return Vector3.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 rotate(Vector3 a, Quaternion b)
        {
            return Vector3.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 rotate(Quaternion a, Vector3 b)
        {
            return Vector3.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 mul(Vector4 a, Quaternion b)
        {
            return Vector4.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 mul(Quaternion a, Vector4 b)
        {
            return Vector4.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 rotate(Vector4 a, Quaternion b)
        {
            return Vector4.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 rotate(Quaternion a, Vector4 b)
        {
            return Vector4.Transform(b, a);
        }
    }
}

