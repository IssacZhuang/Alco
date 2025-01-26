//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 min(int3 a, int3 b)
        {
            return new int3(min(a.x, b.x), min(a.y, b.y), min(a.z, b.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 max(int3 a, int3 b)
        {
            return new int3(max(a.x, b.x), max(a.y, b.y), max(a.z, b.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 abs(int3 a)
        {
            return new int3(abs(a.x), abs(a.y), abs(a.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 select(int3 a, int3 b, bool test)
        {
            return test ? b : a;
        }

    }
}
