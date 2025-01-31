//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 min(Half2 a, Half2 b)
        {
            return new Half2(min(a.x, b.x), min(a.y, b.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 max(Half2 a, Half2 b)
        {
            return new Half2(max(a.x, b.x), max(a.y, b.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 abs(Half2 a)
        {
            return new Half2(abs(a.x), abs(a.y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 lerp(Half2 a, Half2 b, Half t)
        {
            return new Half2(lerp(a.x, b.x, t), lerp(a.y, b.y, t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 select(Half2 a, Half2 b, bool test)
        {
            return test ? b : a;
        }

    }
}
