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
            return new Half2(min(a.X, b.X), min(a.Y, b.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 max(Half2 a, Half2 b)
        {
            return new Half2(max(a.X, b.X), max(a.Y, b.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 abs(Half2 a)
        {
            return new Half2(abs(a.X), abs(a.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 lerp(Half2 a, Half2 b, Half t)
        {
            return new Half2(lerp(a.X, b.X, t), lerp(a.Y, b.Y, t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half2 select(Half2 a, Half2 b, bool test)
        {
            return test ? b : a;
        }

    }
}
