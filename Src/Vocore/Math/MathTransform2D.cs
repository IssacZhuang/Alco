using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform2D inverse(Transform2D a)
        {
            Rotation2D invRot = inverse(a.rotation);
            return new Transform2D
            {
                position = mul(invRot, -a.position),
                rotation = invRot,
                scale = Vector2.One / a.scale
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform2D transform(Transform2D parent, Transform2D child)
        {
            return new Transform2D
            {
                position = mul(parent.rotation, parent.scale * child.position) + parent.position,
                rotation = parent.rotation * child.rotation,
                scale = parent.scale * child.scale
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 transform(Transform2D a, Vector2 b)
        {
            return transform(a.Matrix, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 transform(Matrix4x4 a, Vector2 b)
        {
            return Vector2.Transform(b, a);
        }
    }

}