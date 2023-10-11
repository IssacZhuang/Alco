using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;



namespace Vocore
{
    public static class UtilsTranform
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Tranform ToLocal(Tranform transform, Tranform parent)
        {
            Tranform parentInverse = math.inverse(parent);
            Vector3 localPosition = math.mul(parentInverse.rotation, transform.position - parent.position);
            Quaternion localRotation = math.mul(parentInverse.rotation, transform.rotation);
            return new Tranform(localRotation, localPosition);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Angle(Quaternion a, Quaternion b)
        {
            float dot = math.dot(a, b);
            return math.acos(math.min(math.abs(dot), 1f)) * 2f * math.sign(dot);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateTransform(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateTransform(Vector3 position, Quaternion rotation)
        {
            return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }
    }
}

