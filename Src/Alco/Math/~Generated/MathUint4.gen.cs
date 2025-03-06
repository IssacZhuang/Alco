//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 min(uint4 a, uint4 b)
        {
            return new uint4(min(a.X, b.X), min(a.Y, b.Y), min(a.Z, b.Z), min(a.W, b.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 max(uint4 a, uint4 b)
        {
            return new uint4(max(a.X, b.X), max(a.Y, b.Y), max(a.Z, b.Z), max(a.W, b.W));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint4 select(uint4 a, uint4 b, bool test)
        {
            return test ? b : a;
        }
    }
}
