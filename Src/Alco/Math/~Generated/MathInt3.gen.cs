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
            return new int3(min(a.X, b.X), min(a.Y, b.Y), min(a.Z, b.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 max(int3 a, int3 b)
        {
            return new int3(max(a.X, b.X), max(a.Y, b.Y), max(a.Z, b.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 abs(int3 a)
        {
            return new int3(abs(a.X), abs(a.Y), abs(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int3 select(int3 a, int3 b, bool test)
        {
            return test ? b : a;
        }

    }
}
