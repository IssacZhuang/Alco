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
            Transform2D parentInverse = math.inverse(parent);
            Vector2 localPosition = math.rotate(parentInverse.rotation, transform.position - parent.position) / parent.scale;
            float localRotation = parentInverse.rotation + transform.rotation;

            return new Transform2D(localRotation, localPosition);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform2D inverse(Transform2D a)
        {
            float invRot = -a.rotation;
            return new Transform2D
            {
                position = rotate(invRot, -a.position),
                rotation = invRot
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 transform(Transform2D a, Vector2 b)
        {
            return rotate(b, a.rotation) + a.position;
        }
    }

}