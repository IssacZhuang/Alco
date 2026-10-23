using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        /// <summary>
        /// The half-precision representation of zero.
        /// </summary>
        public static readonly Half FP16Zero = (Half)0.0f;
        /// <summary>
        /// The half-precision representation of one.
        /// </summary>
        public static readonly Half FP16One = (Half)1.0f;
        /// <summary>
        /// The half-precision multiplier that converts degrees to radians.
        /// </summary>
        public static readonly Half FP16TORADIANS = (Half)DegToRad;
        /// <summary>
        /// The half-precision multiplier that converts radians to degrees.
        /// </summary>
        public static readonly Half FP16TODEGREES = (Half)RadToDeg;


        /// <summary>
        /// Returns the smaller of two half-precision values.
        /// </summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half min(Half a, Half b)
        {
            return a < b ? a : b;
        }

        /// <summary>
        /// Returns the larger of two half-precision values.
        /// </summary>
        /// <param name="a">The first value.</param>
        /// <param name="b">The second value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half max(Half a, Half b)
        {
            return a > b ? a : b;
        }

        /// <summary>
        /// Returns the absolute value of a half-precision value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half abs(Half a)
        {
            return a < FP16Zero ? -a : a;
        }


        /// <summary>
        /// Selects between two half-precision values based on a condition.
        /// </summary>
        /// <param name="a">The value returned when the condition is false.</param>
        /// <param name="b">The value returned when the condition is true.</param>
        /// <param name="test">The condition to test.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half select(Half a, Half b, bool test)
        {
            return test ? b : a;
        }

        /// <summary>
        /// Returns the sign of a half-precision value: -1 for negative, 0 for zero, and 1 for positive.
        /// </summary>
        /// <param name="x">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half sign(Half x)
        {
            return (x > FP16Zero ? FP16One : FP16Zero) - (x < FP16Zero ? FP16One : FP16Zero);
        }



        /// <summary>
        /// Clamps a half-precision value to the inclusive range defined by the given bounds.
        /// </summary>
        /// <param name="a">The value to clamp.</param>
        /// <param name="min">The lower bound.</param>
        /// <param name="max">The upper bound.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half clamp(Half a, Half min, Half max)
        {
            return a < min ? min : (a > max ? max : a);
        }

        /// <summary>
        /// Returns the largest integer less than or equal to the value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half floor(Half a)
        {
            return (Half)MathF.Floor((float)a);
        }

        /// <summary>
        /// Returns the smallest integer greater than or equal to the value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half ceil(Half a)
        {
            return (Half)MathF.Ceiling((float)a);
        }

        /// <summary>
        /// Rounds a half-precision value to the nearest integer.
        /// </summary>
        /// <param name="a">The value to round.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half round(Half a)
        {
            return (Half)MathF.Round((float)a);
        }

        /// <summary>
        /// Raises a half-precision value to the specified power.
        /// </summary>
        /// <param name="a">The base value.</param>
        /// <param name="b">The exponent.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half pow(Half a, Half b)
        {
            return (Half)MathF.Pow((float)a, (float)b);
        }

        /// <summary>
        /// Returns the square root of a half-precision value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half sqrt(Half a)
        {
            return (Half)MathF.Sqrt((float)a);
        }

        /// <summary>
        /// Returns the natural logarithm of a half-precision value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half log(Half a)
        {
            return (Half)MathF.Log((float)a);
        }

        /// <summary>
        /// Returns the logarithm of a half-precision value in the specified base.
        /// </summary>
        /// <param name="a">The value whose logarithm is computed.</param>
        /// <param name="b">The logarithm base.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half log(Half a, Half b)
        {
            return (Half)MathF.Log((float)a, (float)b);
        }

        /// <summary>
        /// Returns the base-2 logarithm of a half-precision value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half log2(Half a)
        {
            return (Half)MathF.Log((float)a, 2);
        }

        /// <summary>
        /// Returns the base-10 logarithm of a half-precision value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half log10(Half a)
        {
            return (Half)MathF.Log10((float)a);
        }

        /// <summary>
        /// Converts an angle from degrees to radians.
        /// </summary>
        /// <param name="a">The angle in degrees.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half radians(Half a)
        {
            return a * FP16TORADIANS;
        }

        /// <summary>
        /// Converts an angle from radians to degrees.
        /// </summary>
        /// <param name="a">The angle in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half degrees(Half a)
        {
            return a * FP16TODEGREES;
        }


        /// <summary>
        /// Linearly interpolates between two half-precision values.
        /// </summary>
        /// <param name="a">The start value.</param>
        /// <param name="b">The end value.</param>
        /// <param name="t">The interpolation factor, typically in the range [0, 1].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half lerp(Half a, Half b, Half t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Returns the angle whose cosine is the specified value.
        /// </summary>
        /// <param name="a">The cosine value, in the range [-1, 1].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half acos(Half a)
        {
            return (Half)MathF.Acos((float)a);
        }

        /// <summary>
        /// Returns the angle whose sine is the specified value.
        /// </summary>
        /// <param name="a">The sine value, in the range [-1, 1].</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half asin(Half a)
        {
            return (Half)MathF.Asin((float)a);
        }

        /// <summary>
        /// Returns the angle whose tangent is the specified value.
        /// </summary>
        /// <param name="a">The tangent value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half atan(Half a)
        {
            return (Half)MathF.Atan((float)a);
        }

        /// <summary>
        /// Returns the angle whose tangent is the quotient of two values.
        /// </summary>
        /// <param name="a">The numerator (y coordinate).</param>
        /// <param name="b">The denominator (x coordinate).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half atan2(Half a, Half b)
        {
            return (Half)MathF.Atan2((float)a, (float)b);
        }

        /// <summary>
        /// Returns the cosine of an angle in radians.
        /// </summary>
        /// <param name="a">The angle in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half cos(Half a)
        {
            return (Half)MathF.Cos((float)a);
        }

        /// <summary>
        /// Returns the sine of an angle in radians.
        /// </summary>
        /// <param name="a">The angle in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half sin(Half a)
        {
            return (Half)MathF.Sin((float)a);
        }

        /// <summary>
        /// Returns the tangent of an angle in radians.
        /// </summary>
        /// <param name="a">The angle in radians.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half tan(Half a)
        {
            return (Half)MathF.Tan((float)a);
        }

        /// <summary>
        /// Returns the hyperbolic cosine of a half-precision value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half cosh(Half a)
        {
            return (Half)MathF.Cosh((float)a);
        }

        /// <summary>
        /// Returns the hyperbolic sine of a half-precision value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half sinh(Half a)
        {
            return (Half)MathF.Sinh((float)a);
        }

        /// <summary>
        /// Returns the hyperbolic tangent of a half-precision value.
        /// </summary>
        /// <param name="a">The input value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half tanh(Half a)
        {
            return (Half)MathF.Tanh((float)a);
        }

        /// <summary>
        /// Computes the sine and cosine of an angle in radians.
        /// </summary>
        /// <param name="a">The angle in radians.</param>
        /// <param name="s">Receives the sine of the angle.</param>
        /// <param name="c">Receives the cosine of the angle.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void sincos(Half a, out Half s, out Half c)
        {
            s = sin(a);
            c = cos(a);
        }

        /// <summary>
        /// Reinterprets the bits of an unsigned integer as a half-precision float.
        /// </summary>
        /// <param name="a">The bit pattern to reinterpret.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe static Half asHalf(uint a)
        {
            return *(Half*)&a;
        }

    }
}

