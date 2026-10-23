using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Alco
{
    public static partial class math
    {
        /// <summary>
        /// Returns the smaller of two values.
        /// </summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float min(float a, float b)
        {
            return a < b ? a : b;
        }

        /// <summary>
        /// Returns the larger of two values.
        /// </summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float max(float a, float b)
        {
            return a > b ? a : b;
        }

        /// <summary>
        /// Returns the absolute value of a value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float abs(float a)
        {
            return a < 0 ? -a : a;
        }

        /// <summary>
        /// Selects between two values based on a condition.
        /// </summary>
        /// <param name="a">The value returned when the condition is false.</param>
        /// <param name="b">The value returned when the condition is true.</param>
        /// <param name="test">The condition to test.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float select(float a, float b, bool test)
        {
            return test ? b : a;
        }

        /// <summary>
        /// Returns the sign of a value: -1 for negative, 0 for zero, and 1 for positive.
        /// </summary>
        /// <param name="x">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sign(float x)
        {
            return (x > 0.0f ? 1.0f : 0.0f) - (x < 0.0f ? 1.0f : 0.0f);
        }

        /// <summary>
        /// Clamps a value to the inclusive range defined by the given bounds.
        /// </summary>
        /// <param name="a">The value to clamp.</param>
        /// <param name="min">The lower bound.</param>
        /// <param name="max">The upper bound.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float clamp(float a, float min, float max)
        {
            return a < min ? min : (a > max ? max : a);
        }

        /// <summary>
        /// Returns the largest integer less than or equal to the value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float floor(float a)
        {
            return MathF.Floor(a);
        }

        /// <summary>
        /// Returns the smallest integer greater than or equal to the value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ceil(float a)
        {
            return MathF.Ceiling(a);
        }

        /// <summary>
        /// Rounds a value to the nearest integer.
        /// </summary>
        /// <param name="a">The value to round.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float round(float a)
        {
            return MathF.Round(a);
        }

        /// <summary>
        /// Rounds a value to the specified number of fractional digits.
        /// </summary>
        /// <param name="a">The value to round.</param>
        /// <param name="precision">The number of fractional digits in the result.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float round(float a, int precision)
        {
            return MathF.Round(a, precision);
        }

        /// <summary>
        /// Raises a value to the specified power.
        /// </summary>
        /// <param name="a">The base value.</param>
        /// <param name="b">The exponent.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float pow(float a, float b)
        {
            return MathF.Pow(a, b);
        }

        /// <summary>
        /// Returns the square root of a value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sqrt(float a)
        {
            return MathF.Sqrt(a);
        }

        /// <summary>
        /// Returns an approximate reciprocal square root of a value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static float rsqrt(float a)
        {
            return MathF.ReciprocalSqrtEstimate(a);
        }

        /// <summary>
        /// Returns the natural logarithm of a value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float log(float a)
        {
            return MathF.Log(a);
        }

        /// <summary>
        /// Returns the logarithm of a value in the specified base.
        /// </summary>
        /// <param name="a">The value whose logarithm is computed.</param>
        /// <param name="b">The logarithm base.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float log(float a, float b)
        {
            return MathF.Log(a, b);
        }

        /// <summary>
        /// Returns the base-2 logarithm of a value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float log2(float a)
        {
            return MathF.Log(a, 2);
        }

        /// <summary>
        /// Returns the base-10 logarithm of a value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float log10(float a)
        {
            return MathF.Log10(a);
        }

        /// <summary>
        /// Converts an angle from degrees to radians.
        /// </summary>
        /// <param name="a">The angle in degrees.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float radians(float a)
        {
            return a * DegToRad;
        }

        /// <summary>
        /// Converts an angle from radians to degrees.
        /// </summary>
        /// <param name="a">The angle in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float degrees(float a)
        {
            return a * RadToDeg;
        }

        /// <summary>
        /// Linearly interpolates between two values.
        /// </summary>
        /// <param name="a">The start value.</param>
        /// <param name="b">The end value.</param>
        /// <param name="t">The interpolation factor, typically in the range [0, 1].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Returns the angle whose cosine is the specified value.
        /// </summary>
        /// <param name="a">The cosine value, in the range [-1, 1].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float acos(float a)
        {
            return MathF.Acos(a);
        }

        /// <summary>
        /// Returns the angle whose sine is the specified value.
        /// </summary>
        /// <param name="a">The sine value, in the range [-1, 1].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float asin(float a)
        {
            return MathF.Asin(a);
        }

        /// <summary>
        /// Returns the angle whose tangent is the specified value.
        /// </summary>
        /// <param name="a">The tangent value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float atan(float a)
        {
            return MathF.Atan(a);
        }

        /// <summary>
        /// Returns the angle whose tangent is the quotient of two values.
        /// </summary>
        /// <param name="a">The numerator (y coordinate).</param>
        /// <param name="b">The denominator (x coordinate).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float atan2(float a, float b)
        {
            return MathF.Atan2(a, b);
        }

        /// <summary>
        /// Returns the cosine of an angle in radians.
        /// </summary>
        /// <param name="a">The angle in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float cos(float a)
        {
            return MathF.Cos(a);
        }

        /// <summary>
        /// Returns the sine of an angle in radians.
        /// </summary>
        /// <param name="a">The angle in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sin(float a)
        {
            return MathF.Sin(a);
        }

        /// <summary>
        /// Returns the tangent of an angle in radians.
        /// </summary>
        /// <param name="a">The angle in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float tan(float a)
        {
            return MathF.Tan(a);
        }

        /// <summary>
        /// Returns the hyperbolic cosine of a value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float cosh(float a)
        {
            return MathF.Cosh(a);
        }

        /// <summary>
        /// Returns the hyperbolic sine of a value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sinh(float a)
        {
            return MathF.Sinh(a);
        }

        /// <summary>
        /// Returns the hyperbolic tangent of a value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float tanh(float a)
        {
            return MathF.Tanh(a);
        }

        /// <summary>
        /// Computes the sine and cosine of an angle in radians.
        /// </summary>
        /// <param name="a">The angle in radians.</param>
        /// <param name="s">Receives the sine of the angle.</param>
        /// <param name="c">Receives the cosine of the angle.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(float a, out float s, out float c)
        {
            s = sin(a);
            c = cos(a);
        }

        /// <summary>
        /// Reinterprets the bits of an unsigned integer as a float.
        /// </summary>
        /// <param name="a">The bit pattern to reinterpret.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static float asfloat(uint a)
        {
            return *(float*)&a;
        }

        /// <summary>
        /// Reinterprets the bits of a float as an unsigned integer.
        /// </summary>
        /// <param name="a">The float value to reinterpret.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static uint asuint(float a)
        {
            return *(uint*)&a;
        }

        /// <summary>
        /// Smoothly damps a value toward a target using spring-like motion.
        /// </summary>
        /// <param name="current">The current value.</param>
        /// <param name="target">The target value.</param>
        /// <param name="currentVelocity">The current velocity, updated by this call.</param>
        /// <param name="smoothTime">The approximate time to reach the target, in seconds.</param>
        /// <param name="maxSpeed">The maximum speed.</param>
        /// <param name="deltaTime">The elapsed time since the last call, in seconds.</param>
        /// <returns>The new damped value.</returns>
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

        /// <summary>
        /// Snaps a value to the nearest multiple of an interval around an offset.
        /// </summary>
        /// <param name="value">The value to snap.</param>
        /// <param name="interval">The snapping interval; zero leaves the value unchanged.</param>
        /// <param name="offset">The origin of the snapping grid.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float snap(float value, float interval, float offset)
        {
            if (interval == 0f) return value; // Avoid division by zero
            return round((value - offset) / interval) * interval + offset;
        }

    }
}

