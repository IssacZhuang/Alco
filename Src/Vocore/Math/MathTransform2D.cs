using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform2D toLocal(Transform2D transform, Transform2D parent)
        {
            Transform2D parentInverse = inverse(parent);
            Vector2 localPosition = rotate(parentInverse.rotation, transform.position - parent.position) * parentInverse.scale;
            Rotation2D localRotation = mul(transform.rotation, parentInverse.rotation);
            Vector2 localScale = transform.scale * parentInverse.scale;
            return new Transform2D(localRotation, localPosition);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform2D inverse(Transform2D a)
        {
            return new Transform2D
            {
                position = -a.position,
                rotation = inverse(a.rotation),
                scale = Vector2.One / a.scale
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