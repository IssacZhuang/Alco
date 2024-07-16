//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 min(int4 a, int4 b)
        {
            return new int4(min(a.x, b.x), min(a.y, b.y), min(a.z, b.z), min(a.w, b.w));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 max(int4 a, int4 b)
        {
            return new int4(max(a.x, b.x), max(a.y, b.y), max(a.z, b.z), max(a.w, b.w));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 abs(int4 a)
        {
            return new int4(abs(a.x), abs(a.y), abs(a.z), abs(a.w));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int4 select(int4 a, int4 b, bool test)
        {
            return test ? b : a;
        }

    }
}
