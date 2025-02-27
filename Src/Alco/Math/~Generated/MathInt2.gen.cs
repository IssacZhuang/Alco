//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 min(int2 a, int2 b)
        {
            return new int2(min(a.X, b.X), min(a.Y, b.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 max(int2 a, int2 b)
        {
            return new int2(max(a.X, b.X), max(a.Y, b.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 abs(int2 a)
        {
            return new int2(abs(a.X), abs(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 select(int2 a, int2 b, bool test)
        {
            return test ? b : a;
        }

    }
}
