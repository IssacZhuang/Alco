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
            return new Half3(min(a.x, b.x), min(a.y, b.y), min(a.z, b.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 max(Half3 a, Half3 b)
        {
            return new Half3(max(a.x, b.x), max(a.y, b.y), max(a.z, b.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 abs(Half3 a)
        {
            return new Half3(abs(a.x), abs(a.y), abs(a.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 lerp(Half3 a, Half3 b, Half t)
        {
            return new Half3(lerp(a.x, b.x, t), lerp(a.y, b.y, t), lerp(a.z, b.z, t));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Half3 select(Half3 a, Half3 b, bool test)
        {
            return test ? b : a;
        }

    }
}
