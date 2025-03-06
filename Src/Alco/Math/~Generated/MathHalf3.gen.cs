//auto-generated
using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco
{
    public static partial class math
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 min(Half3 a, Half3 b)
        {
            return new Half3(min(a.X, b.X), min(a.Y, b.Y), min(a.Z, b.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 max(Half3 a, Half3 b)
        {
            return new Half3(max(a.X, b.X), max(a.Y, b.Y), max(a.Z, b.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 abs(Half3 a)
        {
            return new Half3(abs(a.X), abs(a.Y), abs(a.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 lerp(Half3 a, Half3 b, Half t)
        {
            return new Half3(lerp(a.X, b.X, t), lerp(a.Y, b.Y, t), lerp(a.Z, b.Z, t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 select(Half3 a, Half3 b, bool test)
        {
            return test ? b : a;
        }

    }
}
