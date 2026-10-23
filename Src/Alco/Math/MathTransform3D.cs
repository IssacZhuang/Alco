using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {

        /// <summary>
        /// Combines a parent transform with a child transform expressed in the parent's local space.
        /// </summary>
        /// <param name="parent">The parent transform.</param>
        /// <param name="child">The child transform in the parent's local space.</param>
        /// <returns>The combined transform in world space.</returns>
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

        /// <summary>
        /// Converts a child transform from the parent's local space into world space.
        /// </summary>
        /// <param name="parent">The parent transform.</param>
        /// <param name="child">The child transform in the parent's local space.</param>
        /// <returns>The child transform in world space.</returns>
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

        /// <summary>
        /// Converts a world-space transform into the parent's local space.
        /// </summary>
        /// <param name="parent">The parent transform.</param>
        /// <param name="world">The transform in world space.</param>
        /// <returns>The transform in the parent's local space.</returns>
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

        /// <summary>
        /// Transforms a point by the given transform.
        /// </summary>
        /// <param name="a">The transform to apply.</param>
        /// <param name="b">The point to transform.</param>
        /// <returns>The transformed point.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 transform(Transform3D a, Vector3 b)
        {
            return transform(a.Matrix, b);
        }

        /// <summary>
        /// Transforms a point by the given matrix.
        /// </summary>
        /// <param name="a">The matrix to apply.</param>
        /// <param name="b">The point to transform.</param>
        /// <returns>The transformed point.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 transform(Matrix4x4 a, Vector3 b)
        {
            return Vector3.Transform(b, a);
        }


        /// <summary>
        /// Interpolates between two transforms, using spherical interpolation for rotation.
        /// </summary>
        /// <param name="a">The transform at <paramref name="t"/> = 0.</param>
        /// <param name="b">The transform at <paramref name="t"/> = 1.</param>
        /// <param name="t">The interpolation factor.</param>
        /// <returns>The interpolated transform.</returns>
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