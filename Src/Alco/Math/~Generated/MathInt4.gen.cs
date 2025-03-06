//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 min(int4 a, int4 b)
        {
            return new int4(min(a.X, b.X), min(a.Y, b.Y), min(a.Z, b.Z), min(a.W, b.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 max(int4 a, int4 b)
        {
            return new int4(max(a.X, b.X), max(a.Y, b.Y), max(a.Z, b.Z), max(a.W, b.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 abs(int4 a)
        {
            return new int4(abs(a.X), abs(a.Y), abs(a.Z), abs(a.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 select(int4 a, int4 b, bool test)
        {
            return test ? b : a;
        }

    }
}
