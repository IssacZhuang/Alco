using System;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Vocore
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x3 matrix3trs(Vector2 position, Rotation2D rotation, Vector2 scale)
        {
            return matrix3scale(scale) * matrix3rotation(rotation) * matrix3translation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x3 matrix3tr(Vector2 position, Rotation2D rotation)
        {
            return matrix3rotation(rotation) * matrix3translation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x3 matrix3ts(Vector2 position, Vector2 scale)
        {
            return matrix3scale(scale) * matrix3translation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x3 matrix3rs(Rotation2D rotation, Vector2 scale)
        {
            return matrix3scale(scale) * matrix3rotation(rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x3 matrix3translation(Vector2 postition)
        {
            return Matrix3x3.CreateTranslation(postition);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x3 matrix3scale(Vector2 scale)
        {
            return Matrix3x3.CreateScale(scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x3 matrix3rotation(Rotation2D rotation)
        {
            return Matrix3x3.CreateRotation(rotation);
        }

    }
}