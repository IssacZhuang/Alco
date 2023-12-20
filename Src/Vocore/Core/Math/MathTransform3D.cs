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
            Transform3D invParent = inverse(parent);
            Vector3 localPosition = mul(invParent.rotation, transform.position + invParent.position) * invParent.scale;
            Quaternion localRotation = mul(invParent.rotation, transform.rotation);
            Vector3 localScale = transform.scale * invParent.scale;
            return new Transform3D(localPosition, localRotation, localScale);
        }
        

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform3D inverse(Transform3D a)
        {
            return new Transform3D
            {
                position = -a.position,
                rotation = inverse(a.rotation),
                scale = Vector3.One / a.scale
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 transform(Transform3D a, Vector3 b)
        {
            return mul(a.rotation, b) * a.scale + a.position;
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