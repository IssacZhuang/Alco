using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    //math lib for 3d rotation
    public static partial class math
    {
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
        public static Quaternion euler(Vector3 xyz)
        {
            //do not use Quaternion.CreateFromYawPitchRoll because it is Yaw(Y), Pitch(X), Roll(Z) (XNA style)
            //but in Alco Engine, the rotation order is Yaw(Z), Pitch(Y), Roll(X) in left-handed clockwise
            //same as Unreal Engine

            xyz.Y = -xyz.Y;
            xyz.X = -xyz.X;
            Vector3 halfAngles = xyz * 0.5f;
            float sinX = sin(halfAngles.X);
            float cosX = cos(halfAngles.X);
            float sinY = sin(halfAngles.Y);
            float cosY = cos(halfAngles.Y);
            float sinZ = sin(halfAngles.Z);
            float cosZ = cos(halfAngles.Z);

            Quaternion result;

            // Yaw(Z), Pitch(Y), Roll(X) order implementation
            result.X = sinX * cosY * cosZ - cosX * sinY * sinZ;
            result.Y = cosX * sinY * cosZ + sinX * cosY * sinZ;
            result.Z = cosX * cosY * sinZ - sinX * sinY * cosZ;
            result.W = cosX * cosY * cosZ + sinX * sinY * sinZ;

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion euler(float x, float y, float z)
        {
            return euler(new Vector3(x, y, z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 decompose(Quaternion q)
        {
            q = Quaternion.Normalize(q);

            // ZYX order decomposition (yaw(Z), pitch(Y), roll(X))
            float yaw = atan2(2 * (q.W * q.Z + q.X * q.Y), 1 - 2 * (q.Y * q.Y + q.Z * q.Z));
            float pitch = asin(2 * (q.W * q.Y - q.Z * q.X));
            float roll = atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y));

            // Adjust signs to match engine's coordinate system
            return new Vector3(-roll, -pitch, yaw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion direction(Vector3 dir)
        {
            return Quaternion.CreateFromRotationMatrix(Matrix4x4.CreateLookAt(Vector3.Zero, dir, Vector3.UnitY));
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

