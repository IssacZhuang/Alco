using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static partial class math
    {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform3D toLocal(Transform3D transform, Transform3D parent)
        {
            Transform3D parentInverse = math.inverse(parent);
            Vector3 localPosition = math.mul(parentInverse.rotation, transform.position - parent.position) / parent.scale;
            Quaternion localRotation = math.mul(parentInverse.rotation, transform.rotation);

            return new Transform3D(localRotation, localPosition);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform3D inverse(Transform3D a)
        {
            Quaternion invRot = inverse(a.rotation);
            return new Transform3D
            {
                position = mul(invRot, -a.position),
                rotation = invRot
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 transform(Transform3D a, Vector3 b)
        {
            return mul(a.rotation, b) + a.position;
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform3D lerp(Transform3D a, Transform3D b, float t)
        {
            return new Transform3D
            {
                position = lerp(a.position, b.position, t),
                rotation = lerp(a.rotation, b.rotation, t),
                scale = lerp(a.scale, b.scale, t)
            };
        }
    }
}