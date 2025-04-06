using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {

        // 3D

        //[MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4trs(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            //return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
            float x2 = rotation.X + rotation.X;
            float y2 = rotation.Y + rotation.Y;
            float z2 = rotation.Z + rotation.Z;

            float wx2 = rotation.W * x2;
            float wy2 = rotation.W * y2;
            float wz2 = rotation.W * z2;
            float xx2 = rotation.X * x2;
            float xy2 = rotation.X * y2;
            float xz2 = rotation.X * z2;
            float yy2 = rotation.Y * y2;
            float yz2 = rotation.Y * z2;
            float zz2 = rotation.Z * z2;

            Matrix4x4 result;
            result.M11 = scale.X * (1.0f - (yy2 + zz2));
            result.M12 = scale.X * (xy2 + wz2);
            result.M13 = scale.X * (xz2 - wy2);
            result.M14 = 0.0f;
            result.M21 = scale.Y * (xy2 - wz2);
            result.M22 = scale.Y * (1.0f - (xx2 + zz2));
            result.M23 = scale.Y * (yz2 + wx2);
            result.M24 = 0.0f;
            result.M31 = scale.Z * (xz2 + wy2);
            result.M32 = scale.Z * (yz2 - wx2);
            result.M33 = scale.Z * (1.0f - (xx2 + yy2));
            result.M34 = 0.0f;
            result.M41 = position.X;
            result.M42 = position.Y;
            result.M43 = position.Z;
            result.M44 = 1.0f;

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4tr(Vector3 position, Quaternion rotation)
        {
            return Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(position);
            // float x2 = rotation.X + rotation.X;
            // float y2 = rotation.Y + rotation.Y;
            // float z2 = rotation.Z + rotation.Z;

            // float wx2 = rotation.W * x2;
            // float wy2 = rotation.W * y2;
            // float wz2 = rotation.W * z2;
            // float xx2 = rotation.X * x2;
            // float xy2 = rotation.X * y2;
            // float xz2 = rotation.X * z2;
            // float yy2 = rotation.Y * y2;
            // float yz2 = rotation.Y * z2;
            // float zz2 = rotation.Z * z2;

            // Matrix4x4 result;
            // result.M11 = 1.0f - (yy2 + zz2);
            // result.M12 = xy2 + wz2;
            // result.M13 = xz2 - wy2;
            // result.M14 = 0.0f;
            // result.M21 = xy2 - wz2;
            // result.M22 = 1.0f - (xx2 + zz2);
            // result.M23 = yz2 + wx2;
            // result.M24 = 0.0f;
            // result.M31 = xz2 + wy2;
            // result.M32 = yz2 - wx2;
            // result.M33 = 1.0f - (xx2 + yy2);
            // result.M34 = 0.0f;
            // result.M41 = position.X;
            // result.M42 = position.Y;
            // result.M43 = position.Z;
            // result.M44 = 1.0f;

            // return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4ts(Vector3 position, Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateTranslation(position);
            // Matrix4x4 result;
            // result.M11 = scale.X;
            // result.M12 = 0.0f;
            // result.M13 = 0.0f;
            // result.M14 = 0.0f;
            // result.M21 = 0.0f;
            // result.M22 = scale.Y;
            // result.M23 = 0.0f;
            // result.M24 = 0.0f;
            // result.M31 = 0.0f;
            // result.M32 = 0.0f;
            // result.M33 = scale.Z;
            // result.M34 = 0.0f;
            // result.M41 = position.X;
            // result.M42 = position.Y;
            // result.M43 = position.Z;
            // result.M44 = 1.0f;

            // return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4rs(Quaternion rotation, Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation);
            // float xx2 = rotation.X * rotation.X * 2.0f;
            // float yy2 = rotation.Y * rotation.Y * 2.0f;
            // float zz2 = rotation.Z * rotation.Z * 2.0f;
            // float xy2 = rotation.X * rotation.Y * 2.0f;
            // float wz2 = rotation.W * rotation.Z * 2.0f;
            // float xz2 = rotation.X * rotation.Z * 2.0f;
            // float wy2 = rotation.W * rotation.Y * 2.0f;
            // float yz2 = rotation.Y * rotation.Z * 2.0f;
            // float wx2 = rotation.W * rotation.X * 2.0f;

            // Matrix4x4 result;
            // result.M11 = (1.0f - (yy2 + zz2)) * scale.X;
            // result.M12 = (xy2 + wz2) * scale.X;
            // result.M13 = (xz2 - wy2) * scale.X;
            // result.M14 = 0.0f;
            // result.M21 = (xy2 - wz2) * scale.Y;
            // result.M22 = (1.0f - (xx2 + zz2)) * scale.Y;
            // result.M23 = (yz2 + wx2) * scale.Y;
            // result.M24 = 0.0f;
            // result.M31 = (xz2 + wy2) * scale.Z;
            // result.M32 = (yz2 - wx2) * scale.Z;
            // result.M33 = (1.0f - (xx2 + yy2)) * scale.Z;
            // result.M34 = 0.0f;
            // result.M41 = 0.0f;
            // result.M42 = 0.0f;
            // result.M43 = 0.0f;
            // result.M44 = 1.0f;

            // return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4translation(Vector3 position)
        {
            return Matrix4x4.CreateTranslation(position);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4rotation(Quaternion rotation)
        {
            return Matrix4x4.CreateFromQuaternion(rotation);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4scale(Vector3 scale)
        {
            return Matrix4x4.CreateScale(scale);
        }

        // 2D

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4trs(Vector2 position, Rotation2D rotation, Vector2 scale)
        {
            return matrix4scale(scale) * matrix4rotation(rotation) * matrix4translation(position);
            // Matrix4x4 result;
            // result.M11 = rotation.c * scale.X;
            // result.M12 = -rotation.s * scale.X;
            // result.M13 = 0.0f;
            // result.M14 = 0.0f;
            // result.M21 = rotation.s * scale.Y;
            // result.M22 = rotation.c * scale.Y;
            // result.M23 = 0.0f;
            // result.M24 = 0.0f;
            // result.M31 = 0.0f;
            // result.M32 = 0.0f;
            // result.M33 = 1.0f;
            // result.M34 = 0.0f;
            // result.M41 = position.X;
            // result.M42 = position.Y;
            // result.M43 = 0.0f;
            // result.M44 = 1.0f;

            // return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4tr(Vector2 position, Rotation2D rotation)
        {
            return matrix4rotation(rotation) * matrix4translation(position);
            // Matrix4x4 result;
            // result.M11 = rotation.c;
            // result.M12 = -rotation.s;
            // result.M13 = 0.0f;
            // result.M14 = 0.0f;
            // result.M21 = rotation.s;
            // result.M22 = rotation.c;
            // result.M23 = 0.0f;
            // result.M24 = 0.0f;
            // result.M31 = 0.0f;
            // result.M32 = 0.0f;
            // result.M33 = 1.0f;
            // result.M34 = 0.0f;
            // result.M41 = position.X;
            // result.M42 = position.Y;
            // result.M43 = 0.0f;
            // result.M44 = 1.0f;

            // return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4ts(Vector2 position, Vector2 scale)
        {
            return matrix4scale(scale) * matrix4translation(position);
            // Matrix4x4 result;
            // result.M11 = scale.X;
            // result.M12 = 0.0f;
            // result.M13 = 0.0f;
            // result.M14 = 0.0f;
            // result.M21 = 0.0f;
            // result.M22 = scale.Y;
            // result.M23 = 0.0f;
            // result.M24 = 0.0f;
            // result.M31 = 0.0f;
            // result.M32 = 0.0f;
            // result.M33 = 1.0f;
            // result.M34 = 0.0f;
            // result.M41 = position.X;
            // result.M42 = position.Y;
            // result.M43 = 0.0f;
            // result.M44 = 1.0f;

            // return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4rs(Rotation2D rotation, Vector2 scale)
        {
            return matrix4scale(scale) * matrix4rotation(rotation);
            // Matrix4x4 result;
            // result.M11 = rotation.c * scale.X;
            // result.M12 = -rotation.s * scale.X;
            // result.M13 = 0.0f;
            // result.M14 = 0.0f;
            // result.M21 = rotation.s * scale.Y;
            // result.M22 = rotation.c * scale.Y;
            // result.M23 = 0.0f;
            // result.M24 = 0.0f;
            // result.M31 = 0.0f;
            // result.M32 = 0.0f;
            // result.M33 = 1.0f;
            // result.M34 = 0.0f;
            // result.M41 = 0.0f;
            // result.M42 = 0.0f;
            // result.M43 = 0.0f;
            // result.M44 = 1.0f;

            // return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4translation(Vector2 position)
        {
            Matrix4x4 identity = Matrix4x4.Identity;
            identity.M41 = position.X;
            identity.M42 = position.Y;
            return identity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4scale(Vector2 scale)
        {
            Matrix4x4 identity = Matrix4x4.Identity;
            identity.M11 = scale.X;
            identity.M22 = scale.Y;
            return identity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 matrix4rotation(Rotation2D rotation)
        {
            float sin = rotation.S;
            float cos = rotation.C;
            Matrix4x4 identity = Matrix4x4.Identity;
            identity.M11 = cos;
            identity.M12 = -sin;
            identity.M21 = sin;
            identity.M22 = cos;
            return identity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 inverse(in Matrix4x4 matrix)
        {
            if (Matrix4x4.Invert(matrix, out Matrix4x4 result))
            {
                return result;
            }
            else
            {
                return Matrix4x4.Identity;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Matrix4x4 transpose(in Matrix4x4 matrix)
        {
            return Matrix4x4.Transpose(matrix);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float determinant(in Matrix4x4 matrix)
        {
            //left-handed
            return matrix.M11 * (
                matrix.M22 * (matrix.M33 * matrix.M44 - matrix.M34 * matrix.M43) -
                matrix.M23 * (matrix.M32 * matrix.M44 - matrix.M34 * matrix.M42) +
                matrix.M24 * (matrix.M32 * matrix.M43 - matrix.M33 * matrix.M42)
            ) -
            matrix.M12 * (
                matrix.M21 * (matrix.M33 * matrix.M44 - matrix.M34 * matrix.M43) -
                matrix.M23 * (matrix.M31 * matrix.M44 - matrix.M34 * matrix.M41) +
                matrix.M24 * (matrix.M31 * matrix.M43 - matrix.M33 * matrix.M41)
            ) +
            matrix.M13 * (
                matrix.M21 * (matrix.M32 * matrix.M44 - matrix.M34 * matrix.M42) -
                matrix.M22 * (matrix.M31 * matrix.M44 - matrix.M34 * matrix.M41) +
                matrix.M24 * (matrix.M31 * matrix.M42 - matrix.M32 * matrix.M41)
            ) -
            matrix.M14 * (
                matrix.M21 * (matrix.M32 * matrix.M43 - matrix.M33 * matrix.M42) -
                matrix.M22 * (matrix.M31 * matrix.M43 - matrix.M33 * matrix.M41) +
                matrix.M23 * (matrix.M31 * matrix.M42 - matrix.M32 * matrix.M41)
            );
        }

        /// <summary>
        /// Decompose a matrix into scale, rotation, and translation in left-handed coordinate system.
        /// </summary>
        /// <param name="matrix">The matrix to decompose.</param>
        /// <param name="scale">The scale of the matrix.</param>
        /// <param name="rotation">The rotation of the matrix.</param>
        /// <param name="translation">The translation of the matrix.</param>
        public static void decompose(Matrix4x4 matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
        {
            // static void SetAxis(ref Matrix4x4 matrix, int axis, Vector3 vector)
            // {
            //     matrix[axis, 0] = vector.X;
            //     matrix[axis, 1] = vector.Y;
            //     matrix[axis, 2] = vector.Z;
            // }

            // static Vector3 GetScaledAxisX(in Matrix4x4 matrix)
            // {
            //     return new Vector3(matrix.M11, matrix.M12, matrix.M13);
            // }

            ExtractScale(ref matrix, out scale);
            if (determinant(matrix) < 0)
            {
                scale.X = -scale.X;
                //SetAxis(ref matrix, 0, -GetScaledAxisX(matrix));
                matrix.M11 = -matrix.M11;
                matrix.M12 = -matrix.M12;
                matrix.M13 = -matrix.M13;
            }

            ExtractRotation(matrix, out rotation);
            ExtractTranslation(matrix, out translation);

            rotation = Quaternion.Normalize(rotation);
        }

        public static void decompose(in Matrix4x4 matrix, out Vector3 scale, out Vector3 eulerAngles, out Vector3 translation)
        {
            decompose(matrix, out scale, out Quaternion rotation, out translation);
            eulerAngles = euler(rotation);
        }

        public static void decompose(in Matrix4x4 matrix, out Vector2 scale, out Rotation2D rotation, out Vector2 translation)
        {
            decompose(matrix, out Vector3 scale3, out Quaternion rotation2, out Vector3 translation3);
            scale = new Vector2(scale3.X, scale3.Y);
            Vector3 angles = euler(rotation2);
            rotation = Rotation2D.FromDegree(angles.Z);
            translation = new Vector2(translation3.X, translation3.Y);
        }

        public static void decompose(in Matrix4x4 matrix, out Vector2 scale, out float angle, out Vector2 translation)
        {
            decompose(matrix, out Vector3 scale3, out Quaternion rotation2, out Vector3 translation3);
            scale = new Vector2(scale3.X, scale3.Y);
            Vector3 angles = euler(rotation2);
            angle = angles.Z;
            translation = new Vector2(translation3.X, translation3.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExtractTranslation(in Matrix4x4 matrix, out Vector3 translation)
        {
            translation = new Vector3(matrix.M41, matrix.M42, matrix.M43);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe static void ExtractScale(ref Matrix4x4 matrix, out Vector3 scale, float tolerance = 1e-5f)
        {
            //assum tolerance is 0
            float squareSum0 = matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12 + matrix.M13 * matrix.M13;
            float squareSum1 = matrix.M21 * matrix.M21 + matrix.M22 * matrix.M22 + matrix.M23 * matrix.M23;
            float squareSum2 = matrix.M31 * matrix.M31 + matrix.M32 * matrix.M32 + matrix.M33 * matrix.M33;

            if (squareSum0 > tolerance)
            {
                float scale0 = MathF.Sqrt(squareSum0);
                scale.X = scale0;
                float invScale0 = 1.0f / scale0;
                matrix.M11 *= invScale0;
                matrix.M12 *= invScale0;
                matrix.M13 *= invScale0;
            }
            else
            {
                scale.X = 0;
            }

            if (squareSum1 > tolerance)
            {
                float scale1 = MathF.Sqrt(squareSum1);
                scale.Y = scale1;
                float invScale1 = 1.0f / scale1;
                matrix.M21 *= invScale1;
                matrix.M22 *= invScale1;
                matrix.M23 *= invScale1;
            }
            else
            {
                scale.Y = 0;
            }

            if (squareSum2 > tolerance)
            {
                float scale2 = MathF.Sqrt(squareSum2);
                scale.Z = scale2;
                float invScale2 = 1.0f / scale2;
                matrix.M31 *= invScale2;
                matrix.M32 *= invScale2;
                matrix.M33 *= invScale2;
            }
            else
            {
                scale.Z = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ExtractRotation(in Matrix4x4 matrix, out Quaternion rotation)
        {
            rotation = quaternion(matrix);
        }
    }
}