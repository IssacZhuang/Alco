using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint min(uint a, uint b)
        {
            return a < b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint max(uint a, uint b)
        {
            return a > b ? a : b;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint select(uint a, uint b, bool test)
        {
            return test ? b : a;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint clamp(uint x, uint a, uint b)
        {
            return max(a, min(b, x));
        }

    }
}

