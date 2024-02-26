using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static partial class math
    {

        // 3D

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4trs(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4tr(Vector3 position, Quaternion rotation)
        {
            return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4ts(Vector3 position, Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4rs(Quaternion rotation, Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4translation(Vector3 position)
        {
            return Matrix4x4.CreateTranslation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4rotation(Quaternion rotation)
        {
            return Matrix4x4.CreateFromQuaternion(rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4scale(Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale);
        }

        // 2D

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4trs(Vector2 position, Rotation2D rotation, Vector2 scale)
        {
            return matrix4scale(scale) * matrix4rotation(rotation) * matrix4translation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4tr(Vector2 position, Rotation2D rotation)
        {
            return matrix4rotation(rotation) * matrix4translation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4ts(Vector2 position, Vector2 scale)
        {
            return matrix4scale(scale) * matrix4translation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4rs(Rotation2D rotation, Vector2 scale)
        {
            return matrix4scale(scale) * matrix4rotation(rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4translation(Vector2 position)
        {
            Matrix4x4 identity = Matrix4x4.Identity;
            identity.M41 = position.X;
            identity.M42 = position.Y;
            return identity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4scale(Vector2 scale)
        {
            Matrix4x4 identity = Matrix4x4.Identity;
            identity.M11 = scale.X;
            identity.M22 = scale.Y;
            return identity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4rotation(Rotation2D rotation)
        {
            float sin = rotation.s;
            float cos = rotation.c;
            Matrix4x4 identity = Matrix4x4.Identity;
            identity.M11 = cos;
            identity.M12 = -sin;
            identity.M21 = sin;
            identity.M22 = cos;
            return identity;
        }

    }
}