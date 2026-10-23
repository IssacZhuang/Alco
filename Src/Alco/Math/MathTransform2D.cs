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
        public static Transform2D transform(Transform2D parent, Transform2D child)
        {
            return new Transform2D
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
        public static Transform2D toworld(Transform2D parent, Transform2D child)
        {
            return new Transform2D
            {
                Position = mul(parent.Rotation, parent.Scale * child.Position) + parent.Position,
                Rotation = parent.Rotation * child.Rotation,
                Scale = parent.Scale * child.Scale
            };
        }

        /// <summary>
        /// Converts a local position into world space using the parent transform.
        /// </summary>
        /// <param name="parent">The parent transform.</param>
        /// <param name="localPosition">The position in the parent's local space.</param>
        /// <returns>The position in world space.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 toworld(Transform2D parent, Vector2 localPosition)
        {
            return mul(parent.Rotation, parent.Scale * localPosition) + parent.Position;
        }

        /// <summary>
        /// Converts a world-space transform into the parent's local space.
        /// </summary>
        /// <param name="parent">The parent transform.</param>
        /// <param name="world">The transform in world space.</param>
        /// <returns>The transform in the parent's local space.</returns>
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

        /// <summary>
        /// Converts a world-space position into the parent's local space.
        /// </summary>
        /// <param name="parent">The parent transform.</param>
        /// <param name="worldPosition">The position in world space.</param>
        /// <returns>The position in the parent's local space.</returns>
        public static Vector2 tolocal(Transform2D parent, Vector2 worldPosition)
        {
            Rotation2D invRot = inverse(parent.Rotation);
            return mul(invRot, worldPosition - parent.Position) / parent.Scale;
        }

        /// <summary>
        /// Transforms a point by the given transform.
        /// </summary>
        /// <param name="a">The transform to apply.</param>
        /// <param name="b">The point to transform.</param>
        /// <returns>The transformed point.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 transform(Transform2D a, Vector2 b)
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
        public static Vector2 transform(Matrix4x4 a, Vector2 b)
        {
            return Vector2.Transform(b, a);
        }

        /// <summary>
        /// Interpolates between two transforms, using spherical interpolation for rotation.
        /// </summary>
        /// <param name="a">The transform at <paramref name="t"/> = 0.</param>
        /// <param name="b">The transform at <paramref name="t"/> = 1.</param>
        /// <param name="t">The interpolation factor.</param>
        /// <returns>The interpolated transform.</returns>
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