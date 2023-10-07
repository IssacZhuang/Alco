using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static class math
    {
        public const double TORADIANS_DBL = 0.017453292519943296;
        public const float TORADIANS = (float)TORADIANS_DBL;

        public const double PI_DBL = 3.14159265358979323846;
        public const float PI = (float)PI_DBL;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float min(float a, float b)
        {
            return a < b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int min(int a, int b)
        {
            return a < b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 min(int2 a, int2 b)
        {
            return new int2(min(a.x, b.x), min(a.y, b.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 min(Vector2 a, Vector2 b)
        {
            return Vector2.Min(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 min(Vector3 a, Vector3 b)
        {
            return Vector3.Min(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 min(Vector4 a, Vector4 b)
        {
            return Vector4.Min(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float max(float a, float b)
        {
            return a > b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int max(int a, int b)
        {
            return a > b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 max(int2 a, int2 b)
        {
            return new int2(max(a.x, b.x), max(a.y, b.y));
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 max(Vector2 a, Vector2 b)
        {
            return Vector2.Max(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 max(Vector3 a, Vector3 b)
        {
            return Vector3.Max(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 max(Vector4 a, Vector4 b)
        {
            return Vector4.Max(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float abs(float a)
        {
            return a < 0 ? -a : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 abs(Vector2 a)
        {
            return Vector2.Abs(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 abs(Vector3 a)
        {
            return Vector3.Abs(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 abs(Vector4 a)
        {
            return Vector4.Abs(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float select(float a, float b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 select(Vector2 a, Vector2 b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 select(Vector3 a, Vector3 b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 select(Vector4 a, Vector4 b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sign(float x)
        {
            return (x > 0.0f ? 1.0f : 0.0f) - (x < 0.0f ? 1.0f : 0.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 sign(Vector2 x)
        {
            return new Vector2(sign(x.X), sign(x.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 sign(Vector3 x)
        {
            return new Vector3(sign(x.X), sign(x.Y), sign(x.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 sign(Vector4 x)
        {
            return new Vector4(sign(x.X), sign(x.Y), sign(x.Z), sign(x.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float clamp(float a, float min, float max)
        {
            return a < min ? min : (a > max ? max : a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 clamp(Vector2 a, Vector2 min, Vector2 max)
        {
            return Vector2.Clamp(a, min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 clamp(Vector3 a, Vector3 min, Vector3 max)
        {
            return Vector3.Clamp(a, min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 clamp(Vector4 a, Vector4 min, Vector4 max)
        {
            return Vector4.Clamp(a, min, max);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 clamp(Vector2 a, float min, float max)
        {
            return Vector2.Clamp(a, new Vector2(min), new Vector2(max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 clamp(Vector3 a, float min, float max)
        {
            return Vector3.Clamp(a, new Vector3(min), new Vector3(max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 clamp(Vector4 a, float min, float max)
        {
            return Vector4.Clamp(a, new Vector4(min), new Vector4(max));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float floor(float a)
        {
            return MathF.Floor(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 floor(Vector2 a)
        {
            return new Vector2(floor(a.X), floor(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 floor(Vector3 a)
        {
            return new Vector3(floor(a.X), floor(a.Y), floor(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 floor(Vector4 a)
        {
            return new Vector4(floor(a.X), floor(a.Y), floor(a.Z), floor(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ceil(float a)
        {
            return MathF.Ceiling(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 ceil(Vector2 a)
        {
            return new Vector2(ceil(a.X), ceil(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 ceil(Vector3 a)
        {
            return new Vector3(ceil(a.X), ceil(a.Y), ceil(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 ceil(Vector4 a)
        {
            return new Vector4(ceil(a.X), ceil(a.Y), ceil(a.Z), ceil(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 mul(Vector2 a, Quaternion b)
        {
            return Vector2.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 mul(Quaternion a, Vector2 b)
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
        public static Vector4 mul(Vector4 a, Quaternion b)
        {
            return Vector4.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion mul(Quaternion a, Quaternion b)
        {
            return Quaternion.Multiply(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 mul(Quaternion a, Vector4 b)
        {
            return Vector4.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Vector2 a, Quaternion b)
        {
            return Vector2.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 rotate(Vector3 a, Quaternion b)
        {
            return Vector3.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 rotate(Vector4 a, Quaternion b)
        {
            return Vector4.Transform(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 rotate(Quaternion a, Vector2 b)
        {
            return Vector2.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 rotate(Quaternion a, Vector3 b)
        {
            return Vector3.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 rotate(Quaternion a, Vector4 b)
        {
            return Vector4.Transform(b, a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 inverse(Vector2 a)
        {
            return Vector2.One / a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 inverse(Vector3 a)
        {
            return Vector3.One / a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 inverse(Vector4 a)
        {
            return Vector4.One / a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion inverse(Quaternion a)
        {
            return Quaternion.Inverse(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RigidTransform inverse(RigidTransform a)
        {
            Quaternion invRot = inverse(a.rot);
            return new RigidTransform
            {
                pos = mul(invRot, -a.pos),
                rot = invRot
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 transform(RigidTransform a, Vector3 b)
        {
            return mul(a.rot, b) + a.pos;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Vector2 a, Vector2 b)
        {
            return Vector2.Dot(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Vector3 a, Vector3 b)
        {
            return Vector3.Dot(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Vector4 a, Vector4 b)
        {
            return Vector4.Dot(a, b);
        }

        //todo: use simd
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float dot(Quaternion a, Quaternion b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float pow(float a, float b)
        {
            return MathF.Pow(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 pow(Vector2 a, float b)
        {
            return new Vector2(pow(a.X, b), pow(a.Y, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 pow(Vector2 a, Vector2 b)
        {
            return new Vector2(pow(a.X, b.X), pow(a.Y, b.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 pow(Vector3 a, float b)
        {
            return new Vector3(pow(a.X, b), pow(a.Y, b), pow(a.Z, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 pow(Vector3 a, Vector3 b)
        {
            return new Vector3(pow(a.X, b.X), pow(a.Y, b.Y), pow(a.Z, b.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 pow(Vector4 a, float b)
        {
            return new Vector4(pow(a.X, b), pow(a.Y, b), pow(a.Z, b), pow(a.W, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 pow(Vector4 a, Vector4 b)
        {
            return new Vector4(pow(a.X, b.X), pow(a.Y, b.Y), pow(a.Z, b.Z), pow(a.W, b.W));
        }

        

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 normalize(Vector2 a)
        {
            return Vector2.Normalize(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 normalize(Vector3 a)
        {
            return Vector3.Normalize(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 normalize(Vector4 a)
        {
            return Vector4.Normalize(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sqrt(float a)
        {
            return MathF.Sqrt(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float radians(float a)
        {
            return a * TORADIANS;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 radians(Vector2 a)
        {
            return a * TORADIANS;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 radians(Vector3 a)
        {
            return a * TORADIANS;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 radians(Vector4 a)
        {
            return a * TORADIANS;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 lerp(Vector2 a, Vector2 b, float t)
        {
            return Vector2.Lerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 lerp(Vector3 a, Vector3 b, float t)
        {
            return Vector3.Lerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 lerp(Vector4 a, Vector4 b, float t)
        {
            return Vector4.Lerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion lerp(Quaternion a, Quaternion b, float t)
        {
            return Quaternion.Lerp(a, b, t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float acos(float a)
        {
            return MathF.Acos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 acos(Vector2 a)
        {
            return new Vector2(acos(a.X), acos(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 acos(Vector3 a)
        {
            return new Vector3(acos(a.X), acos(a.Y), acos(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 acos(Vector4 a)
        {
            return new Vector4(acos(a.X), acos(a.Y), acos(a.Z), acos(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float asin(float a)
        {
            return MathF.Asin(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 asin(Vector2 a)
        {
            return new Vector2(asin(a.X), asin(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 asin(Vector3 a)
        {
            return new Vector3(asin(a.X), asin(a.Y), asin(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 asin(Vector4 a)
        {
            return new Vector4(asin(a.X), asin(a.Y), asin(a.Z), asin(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float atan(float a)
        {
            return MathF.Atan(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 atan(Vector2 a)
        {
            return new Vector2(atan(a.X), atan(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 atan(Vector3 a)
        {
            return new Vector3(atan(a.X), atan(a.Y), atan(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 atan(Vector4 a)
        {
            return new Vector4(atan(a.X), atan(a.Y), atan(a.Z), atan(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float atan2(float a, float b)
        {
            return MathF.Atan2(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 atan2(Vector2 a, Vector2 b)
        {
            return new Vector2(atan2(a.X, b.X), atan2(a.Y, b.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 atan2(Vector3 a, Vector3 b)
        {
            return new Vector3(atan2(a.X, b.X), atan2(a.Y, b.Y), atan2(a.Z, b.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 atan2(Vector4 a, Vector4 b)
        {
            return new Vector4(atan2(a.X, b.X), atan2(a.Y, b.Y), atan2(a.Z, b.Z), atan2(a.W, b.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float cos(float a)
        {
            return MathF.Cos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 cos(Vector2 a)
        {
            return new Vector2(cos(a.X), cos(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 cos(Vector3 a)
        {
            return new Vector3(cos(a.X), cos(a.Y), cos(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 cos(Vector4 a)
        {
            return new Vector4(cos(a.X), cos(a.Y), cos(a.Z), cos(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sin(float a)
        {
            return MathF.Sin(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 sin(Vector2 a)
        {
            return new Vector2(sin(a.X), sin(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 sin(Vector3 a)
        {
            return new Vector3(sin(a.X), sin(a.Y), sin(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 sin(Vector4 a)
        {
            return new Vector4(sin(a.X), sin(a.Y), sin(a.Z), sin(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float tan(float a)
        {
            return MathF.Tan(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 tan(Vector2 a)
        {
            return new Vector2(tan(a.X), tan(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 tan(Vector3 a)
        {
            return new Vector3(tan(a.X), tan(a.Y), tan(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 tan(Vector4 a)
        {
            return new Vector4(tan(a.X), tan(a.Y), tan(a.Z), tan(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float cosh(float a)
        {
            return MathF.Cosh(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 cosh(Vector2 a)
        {
            return new Vector2(cosh(a.X), cosh(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 cosh(Vector3 a)
        {
            return new Vector3(cosh(a.X), cosh(a.Y), cosh(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 cosh(Vector4 a)
        {
            return new Vector4(cosh(a.X), cosh(a.Y), cosh(a.Z), cosh(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sinh(float a)
        {
            return MathF.Sinh(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 sinh(Vector2 a)
        {
            return new Vector2(sinh(a.X), sinh(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 sinh(Vector3 a)
        {
            return new Vector3(sinh(a.X), sinh(a.Y), sinh(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 sinh(Vector4 a)
        {
            return new Vector4(sinh(a.X), sinh(a.Y), sinh(a.Z), sinh(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float tanh(float a)
        {
            return MathF.Tanh(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector2 tanh(Vector2 a)
        {
            return new Vector2(tanh(a.X), tanh(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 tanh(Vector3 a)
        {
            return new Vector3(tanh(a.X), tanh(a.Y), tanh(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector4 tanh(Vector4 a)
        {
            return new Vector4(tanh(a.X), tanh(a.Y), tanh(a.Z), tanh(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(float a, out float s, out float c)
        {
            s = sin(a);
            c = cos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Vector2 a, out Vector2 s, out Vector2 c)
        {
            s = sin(a);
            c = cos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Vector3 a, out Vector3 s, out Vector3 c)
        {
            s = sin(a);
            c = cos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Vector4 a, out Vector4 s, out Vector4 c)
        {
            s = sin(a);
            c = cos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Euler(Vector3 xyz)
        {
            return Quaternion.CreateFromYawPitchRoll(xyz.Y, xyz.X, xyz.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion Euler(float x, float y, float z)
        {
            return Quaternion.CreateFromYawPitchRoll(y, x, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion EulerXYZ(Vector3 xyz)
        {
            return Quaternion.CreateFromYawPitchRoll(xyz.Y, xyz.X, xyz.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Quaternion EulerXYZ(float x, float y, float z)
        {
            return Quaternion.CreateFromYawPitchRoll(y, x, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static float asfloat(uint a)
        {
            return *(float*)&a;
        }





    }
}