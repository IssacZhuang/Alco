using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Collections.Generic;



namespace Vocore
{
    public static class UtilsTranform
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform ToLocal(Transform transform, Transform parent)
        {
            Transform parentInverse = math.inverse(parent);
            Vector3 localPosition = math.mul(parentInverse.rotation, transform.position - parent.position);
            Quaternion localRotation = math.mul(parentInverse.rotation, transform.rotation);
            return new Transform(localRotation, localPosition);
        }

        

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateTransformTRS(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateTransformTR(Vector3 position, Quaternion rotation)
        {
            return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
        }

        public static Matrix4x4 CreateTransformTS(Vector3 position, Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 CreateTransformRS(Quaternion rotation, Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation);
        }
    }
}

