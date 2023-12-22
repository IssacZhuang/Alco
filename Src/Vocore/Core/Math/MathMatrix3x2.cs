using System;
using System.Numerics;
using System.Runtime.CompilerServices;


namespace Vocore
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x2 matrix3trs(Vector2 position, Rotation2D rotation, Vector2 scale)
        {
            return matrix3translation(position) * matrix3rotation(rotation) * matrix3scale(scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x2 matrix3translation(Vector2 postition)
        {
            return Matrix3x2.CreateTranslation(postition);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x2 matrix3scale(Vector2 scale)
        {
            return Matrix3x2.CreateScale(scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix3x2 matrix3rotation(Rotation2D rotation)
        {
            Matrix3x2 identity = Matrix3x2.Identity;
            identity.M11 = rotation.c;
            identity.M12 = rotation.s;
            identity.M21 = 0f - rotation.s;
            identity.M22 = rotation.c;
            return identity;
        }

    }
}