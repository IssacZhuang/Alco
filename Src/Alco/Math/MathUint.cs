using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        /// <summary>
        /// Returns the smaller of two unsigned integers.
        /// </summary>
        /// <param name="a">The first integer.</param>
        /// <param name="b">The second integer.</param>
        /// <returns>The smaller integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint min(uint a, uint b)
        {
            return a < b ? a : b;
        }

        /// <summary>
        /// Returns the larger of two unsigned integers.
        /// </summary>
        /// <param name="a">The first integer.</param>
        /// <param name="b">The second integer.</param>
        /// <returns>The larger integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint max(uint a, uint b)
        {
            return a > b ? a : b;
        }

        /// <summary>
        /// Returns <paramref name="b"/> when <paramref name="test"/> is true, otherwise <paramref name="a"/>.
        /// </summary>
        /// <param name="a">The value returned when the test is false.</param>
        /// <param name="b">The value returned when the test is true.</param>
        /// <param name="test">The selection condition.</param>
        /// <returns>The selected value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint select(uint a, uint b, bool test)
        {
            return test ? b : a;
        }

        /// <summary>
        /// Clamps an unsigned integer to the inclusive range [<paramref name="a"/>, <paramref name="b"/>].
        /// </summary>
        /// <param name="x">The integer to clamp.</param>
        /// <param name="a">The lower bound.</param>
        /// <param name="b">The upper bound.</param>
        /// <returns>The clamped integer.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint clamp(uint x, uint a, uint b)
        {
            return max(a, min(b, x));
        }

    }
}

