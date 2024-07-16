//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 min(uint2 a, uint2 b)
        {
            return new uint2(min(a.x, b.x), min(a.y, b.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 max(uint2 a, uint2 b)
        {
            return new uint2(max(a.x, b.x), max(a.y, b.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint2 select(uint2 a, uint2 b, bool test)
        {
            return test ? b : a;
        }
    }
}
