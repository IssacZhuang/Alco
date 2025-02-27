using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform2D transform(Transform2D parent, Transform2D child)
        {
            return new Transform2D
            {
                Position = mul(parent.Rotation, parent.Scale * child.Position) + parent.Position,
                Rotation = parent.Rotation * child.Rotation,
                Scale = parent.Scale * child.Scale
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform2D toworld(Transform2D parent, Transform2D child)
        {
            return new Transform2D
            {
                Position = mul(parent.Rotation, parent.Scale * child.Position) + parent.Position,
                Rotation = parent.Rotation * child.Rotation,
                Scale = parent.Scale * child.Scale
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 toworld(Transform2D parent, Vector2 localPosition)
        {
            return mul(parent.Rotation, parent.Scale * localPosition) + parent.Position;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform2D tolocal(Transform2D parent, Transform2D world)
        {
            Rotation2D invRot = inverse(parent.Rotation);
            return new Transform2D
            {
                Position = mul(invRot, world.Position - parent.Position) / parent.Scale,
                Rotation = invRot * world.Rotation,
                Scale = world.Scale / parent.Scale
            };
        }

        public static Vector2 tolocal(Transform2D parent, Vector2 worldPosition)
        {
            Rotation2D invRot = inverse(parent.Rotation);
            return mul(invRot, worldPosition - parent.Position) / parent.Scale;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform2D lerp(Transform2D a, Transform2D b, float t)
        {
            return new Transform2D
            {
                Position = lerp(a.Position, b.Position, t),
                Rotation = slerp(a.Rotation, b.Rotation, t),
                Scale = lerp(a.Scale, b.Scale, t)
            };
        }
    }

}