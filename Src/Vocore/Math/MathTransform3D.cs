using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static partial class math
    {


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform3D inverse(Transform3D a)
        {
            Quaternion invRot = inverse(a.rotation);
            return new Transform3D
            {
                position = mul(invRot, -a.position),
                rotation = invRot,
                scale = Vector3.One / a.scale
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform3D transform(Transform3D parent, Transform3D child)
        {
            return new Transform3D
            {
                position = mul(parent.rotation, parent.scale * child.position) + parent.position,
                rotation = parent.rotation* child.rotation,
                scale = parent.scale * child.scale
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 transform(Transform3D a, Vector3 b)
        {
            return transform(a.Matrix, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 transform(Matrix4x4 a, Vector3 b)
        {
            return Vector3.Transform(b, a);
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