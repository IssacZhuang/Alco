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
            return new Half4(min(a.X, b.X), min(a.Y, b.Y), min(a.Z, b.Z), min(a.W, b.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 max(Half4 a, Half4 b)
        {
            return new Half4(max(a.X, b.X), max(a.Y, b.Y), max(a.Z, b.Z), max(a.W, b.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 abs(Half4 a)
        {
            return new Half4(abs(a.X), abs(a.Y), abs(a.Z), abs(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 lerp(Half4 a, Half4 b, Half t)
        {
            return new Half4(lerp(a.X, b.X, t), lerp(a.Y, b.Y, t), lerp(a.Z, b.Z, t), lerp(a.W, b.W, t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half4 select(Half4 a, Half4 b, bool test)
        {
            return test ? b : a;
        }

    }
}
