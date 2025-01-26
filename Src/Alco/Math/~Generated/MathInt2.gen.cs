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
            return new int2(min(a.x, b.x), min(a.y, b.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 max(int2 a, int2 b)
        {
            return new int2(max(a.x, b.x), max(a.y, b.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 abs(int2 a)
        {
            return new int2(abs(a.x), abs(a.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int2 select(int2 a, int2 b, bool test)
        {
            return test ? b : a;
        }

    }
}
