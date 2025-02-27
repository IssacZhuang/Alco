//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 min(uint3 a, uint3 b)
        {
            return new uint3(min(a.X, b.X), min(a.Y, b.Y), min(a.Z, b.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 max(uint3 a, uint3 b)
        {
            return new uint3(max(a.X, b.X), max(a.Y, b.Y), max(a.Z, b.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint3 select(uint3 a, uint3 b, bool test)
        {
            return test ? b : a;
        }
    }
}
