using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform3D transform(Transform3D parent, Transform3D child)
        {
            return new Transform3D
            {
                Position = mul(parent.Rotation, parent.Scale * child.Position) + parent.Position,
                Rotation = parent.Rotation * child.Rotation,
                Scale = parent.Scale * child.Scale
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform3D toworld(Transform3D parent, Transform3D child)
        {
            return new Transform3D
            {
                Position = mul(parent.Rotation, parent.Scale * child.Position) + parent.Position,
                Rotation = parent.Rotation* child.Rotation,
                Scale = parent.Scale * child.Scale
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Transform3D tolocal(Transform3D parent, Transform3D world)
        {
            Quaternion invRot = inverse(parent.Rotation);
            return new Transform3D
            {
                Position = mul(invRot, world.Position - parent.Position) / parent.Scale,
                Rotation = invRot * world.Rotation,
                Scale = world.Scale / parent.Scale
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
                Position = lerp(a.Position, b.Position, t),
                Rotation = slerp(a.Rotation, b.Rotation, t),
                Scale = lerp(a.Scale, b.Scale, t)
            };
        }
    }
}