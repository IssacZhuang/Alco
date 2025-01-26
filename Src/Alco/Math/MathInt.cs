using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int min(int a, int b)
        {
            return a < b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int max(int a, int b)
        {
            return a > b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int abs(int a)
        {
            return a < 0 ? -a : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int select(int a, int b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int sign(int x)
        {
            return (x > 0 ? 1 : 0) - (x < 0 ? 1 : 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int clamp(int x, int a, int b)
        {
            return max(a, min(b, x));
        }

    }
}

