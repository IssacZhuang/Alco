using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float min(float a, float b)
        {
            return a < b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float max(float a, float b)
        {
            return a > b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float abs(float a)
        {
            return a < 0 ? -a : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float select(float a, float b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sign(float x)
        {
            return (x > 0.0f ? 1.0f : 0.0f) - (x < 0.0f ? 1.0f : 0.0f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float clamp(float a, float min, float max)
        {
            return a < min ? min : (a > max ? max : a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float floor(float a)
        {
            return MathF.Floor(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ceil(float a)
        {
            return MathF.Ceiling(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float round(float a)
        {
            return MathF.Round(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float pow(float a, float b)
        {
            return MathF.Pow(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sqrt(float a)
        {
            return MathF.Sqrt(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float log(float a)
        {
            return MathF.Log(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float log(float a, float b)
        {
            return MathF.Log(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float log2(float a)
        {
            return MathF.Log(a, 2);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float log10(float a)
        {
            return MathF.Log10(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float radians(float a)
        {
            return a * TO_RADIANS;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float acos(float a)
        {
            return MathF.Acos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float asin(float a)
        {
            return MathF.Asin(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float atan(float a)
        {
            return MathF.Atan(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float atan2(float a, float b)
        {
            return MathF.Atan2(a, b);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float cos(float a)
        {
            return MathF.Cos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sin(float a)
        {
            return MathF.Sin(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float tan(float a)
        {
            return MathF.Tan(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float cosh(float a)
        {
            return MathF.Cosh(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sinh(float a)
        {
            return MathF.Sinh(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float tanh(float a)
        {
            return MathF.Tanh(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(float a, out float s, out float c)
        {
            s = sin(a);
            c = cos(a);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static float asfloat(uint a)
        {
            return *(float*)&a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float damp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime)
        {
            smoothTime = max(0.0001f, smoothTime);
            float num = 2f / smoothTime;
            float num2 = num * deltaTime;
            float num3 = 1f / (1f + num2 + 0.48f * num2 * num2 + 0.235f * num2 * num2 * num2);
            float value = current - target;
            float num4 = target;
            float num5 = maxSpeed * smoothTime;
            value = clamp(value, 0f - num5, num5);
            target = current - value;
            float num6 = (currentVelocity + num * value) * deltaTime;
            currentVelocity = (currentVelocity - num * num6) * num3;
            float num7 = target + (value + num6) * num3;
            if (num4 - current > 0f == num7 > num4)
            {
                num7 = num4;
                currentVelocity = (num7 - num4) / deltaTime;
            }
            return num7;
        }

    }
}

