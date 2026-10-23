using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        /// <summary>
        /// Returns the smaller of two integers.
        /// </summary>
        /// <param name="a">The first integer.</param>
        /// <param name="b">The second integer.</param>
        /// <returns>The smaller integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int min(int a, int b)
        {
            return a < b ? a : b;
        }

        /// <summary>
        /// Returns the larger of two integers.
        /// </summary>
        /// <param name="a">The first integer.</param>
        /// <param name="b">The second integer.</param>
        /// <returns>The larger integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int max(int a, int b)
        {
            return a > b ? a : b;
        }

        /// <summary>
        /// Returns the absolute value of an integer.
        /// </summary>
        /// <param name="a">The integer.</param>
        /// <returns>The absolute value of <paramref name="a"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int abs(int a)
        {
            return a < 0 ? -a : a;
        }

        /// <summary>
        /// Returns <paramref name="b"/> when <paramref name="test"/> is true, otherwise <paramref name="a"/>.
        /// </summary>
        /// <param name="a">The value returned when the test is false.</param>
        /// <param name="b">The value returned when the test is true.</param>
        /// <param name="test">The selection condition.</param>
        /// <returns>The selected value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int select(int a, int b, bool test)
        {
            return test ? b : a;
        }

        /// <summary>
        /// Returns -1, 0 or 1 according to the sign of the integer.
        /// </summary>
        /// <param name="x">The integer.</param>
        /// <returns>The sign of <paramref name="x"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int sign(int x)
        {
            return (x > 0 ? 1 : 0) - (x < 0 ? 1 : 0);
        }

        /// <summary>
        /// Clamps an integer to the inclusive range [<paramref name="a"/>, <paramref name="b"/>].
        /// </summary>
        /// <param name="x">The integer to clamp.</param>
        /// <param name="a">The lower bound.</param>
        /// <param name="b">The upper bound.</param>
        /// <returns>The clamped integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int clamp(int x, int a, int b)
        {
            return max(a, min(b, x));
        }

    }
}

