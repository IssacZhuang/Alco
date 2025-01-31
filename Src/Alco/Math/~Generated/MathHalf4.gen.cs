//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 min(Half4 a, Half4 b)
        {
            return new Half4(min(a.x, b.x), min(a.y, b.y), min(a.z, b.z), min(a.w, b.w));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 max(Half4 a, Half4 b)
        {
            return new Half4(max(a.x, b.x), max(a.y, b.y), max(a.z, b.z), max(a.w, b.w));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 abs(Half4 a)
        {
            return new Half4(abs(a.x), abs(a.y), abs(a.z), abs(a.w));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 lerp(Half4 a, Half4 b, Half t)
        {
            return new Half4(lerp(a.x, b.x, t), lerp(a.y, b.y, t), lerp(a.z, b.z, t), lerp(a.w, b.w, t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 select(Half4 a, Half4 b, bool test)
        {
            return test ? b : a;
        }

    }
}
