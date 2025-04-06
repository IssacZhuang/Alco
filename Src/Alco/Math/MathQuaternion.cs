using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    //math lib for 3d rotation
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion quaternion(Vector3 degrees)
        {
            //do not use Quaternion.CreateFromYawPitchRoll because it is Yaw(Y), Pitch(X), Roll(Z) (XNA style)
            //but in Alco Engine, the rotation order is Yaw(Z), Pitch(Y), Roll(X) in left-handed clockwise
            //same as Unreal Engine

            Vector3 halfAngles = degrees * 0.5f * DegToRad;

            //simd
            (Vector3 sin, Vector3 cos) = Vector3.SinCos(halfAngles);

            Quaternion result;

            result.X = cos.X * sin.Y * sin.Z - sin.X * cos.Y * cos.Z;
            result.Y = -cos.X * sin.Y * cos.Z - sin.X * cos.Y * sin.Z;
            result.Z = cos.X * cos.Y * sin.Z - sin.X * sin.Y * cos.Z;
            result.W = cos.X * cos.Y * cos.Z + sin.X * sin.Y * sin.Z;

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion quaternion(float x, float y, float z)
        {
            return quaternion(new Vector3(x, y, z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static Quaternion quaternion(in Matrix4x4 matrix)
        {
            float trace = matrix.M11 + matrix.M22 + matrix.M33;

            Quaternion q;

            if (trace > 0.0f)
            {
                float invS = rsqrt(trace + 1.0f);
                q.W = 0.5f * (1.0f / invS);
                invS *= 0.5f;

                q.X = (matrix.M23 - matrix.M32) * invS;
                q.Y = (matrix.M31 - matrix.M13) * invS;
                q.Z = (matrix.M12 - matrix.M21) * invS;

            }
            else
            {
                int i = 0;

                if (matrix.M11 < matrix.M22)
                    i = 1;

                if (matrix.M33 < matrix[i, i])
                    i = 2;

                int* nxt = stackalloc int[3] { 1, 2, 0 };
                int j = nxt[i];
                int k = nxt[j];

                float s = matrix[i, i] - matrix[j, j] - matrix[k, k] + 1.0f;
                float invS = rsqrt(s);

                float* qt = stackalloc float[4];
                qt[i] = 0.5f * (1.0f / invS);
                invS *= 0.5f;

                qt[3] = (matrix[j, k] - matrix[k, j]) * invS;
                qt[j] = (matrix[i, j] + matrix[j, i]) * invS;
                qt[k] = (matrix[i, k] + matrix[k, i]) * invS;

                q.X = qt[0];
                q.Y = qt[1];
                q.Z = qt[2];
                q.W = qt[3];
            }
            return q;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion mul(Quaternion a, Quaternion b)
        {
            return Quaternion.Multiply(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion inverse(Quaternion a)
        {
            return Quaternion.Inverse(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion lerp(Quaternion a, Quaternion b, float t)
        {
            return Quaternion.Lerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion slerp(Quaternion a, Quaternion b, float t)
        {
            return Quaternion.Slerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float angle(Quaternion a, Quaternion b)
        {
            float dot = math.dot(a, b);
            return math.acos(math.min(math.abs(dot), 1f)) * 2f * math.sign(dot);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 euler(Quaternion q)
        {
            //decompose quaternion to euler angles Roll(X), Pitch(Y), Yaw(Z) in radians

            float singularityTest = q.Z * q.X - q.W * q.Y;
            float yawY = 2 * (q.W * q.Z + q.X * q.Y);
            float yawX = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);

            const float SINGULARITY_THRESHOLD = 0.4999995f;

            float pitch, yaw, roll;

            if (singularityTest < -SINGULARITY_THRESHOLD)
            {
                pitch = -PI * 0.5f;
                yaw = atan2(yawY, yawX);
                roll = -yaw - (2 * atan2(q.X, q.W));
            }
            else if (singularityTest > SINGULARITY_THRESHOLD)
            {
                pitch = PI * 0.5f;
                yaw = atan2(yawY, yawX);
                roll = yaw - (2 * atan2(q.X, q.W));
            }
            else
            {
                pitch = asin(2 * singularityTest);
                yaw = atan2(yawY, yawX);
                roll = atan2(-2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));
            }

            return new Vector3(roll, pitch, yaw) * RadToDeg;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion direction(Vector3 dir)
        {
            return quaternion(Matrix4x4.CreateLookAtLeftHanded(Vector3.Zero, dir, Vector3.UnitZ));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 direction(Quaternion q)
        {
            return mul(q, Vector3.UnitZ);
        }

        //todo: use simd
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Quaternion a, Quaternion b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 mul(Quaternion a, Vector2 b)
        {
            return Vector2.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 mul(Vector2 a, Quaternion b)
        {
            return Vector2.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Vector2 a, Quaternion b)
        {
            return Vector2.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Quaternion a, Vector2 b)
        {
            return Vector2.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 mul(Vector3 a, Quaternion b)
        {
            return Vector3.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 mul(Quaternion a, Vector3 b)
        {
            return Vector3.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 rotate(Vector3 a, Quaternion b)
        {
            return Vector3.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 rotate(Quaternion a, Vector3 b)
        {
            return Vector3.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 mul(Vector4 a, Quaternion b)
        {
            return Vector4.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 mul(Quaternion a, Vector4 b)
        {
            return Vector4.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 rotate(Vector4 a, Quaternion b)
        {
            return Vector4.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 rotate(Quaternion a, Vector4 b)
        {
            return Vector4.Transform(b, a);
        }
    }
}

