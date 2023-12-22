using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static partial class math
    {

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public static Matrix4x4 CreateTransformTRS(Vector3 position, Quaternion rotation, Vector3 scale)
        // {
        //     return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        // }

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public static Matrix4x4 CreateTransformTR(Vector3 position, Quaternion rotation)
        // {
        //     return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        // }

        // public static Matrix4x4 CreateTransformTS(Vector3 position, Vector3 scale)
        // {
        //     return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position);
        // }

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public static Matrix4x4 CreateTransformRS(Quaternion rotation, Vector3 scale)
        // {
        //     return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation);
        // }

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

        

    }
}