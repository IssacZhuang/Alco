using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Vocore
{
    public static class UitlsNullable
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty<T>(this IEnumerable<T>? list)
        {
            return list == null || list.Count() == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty<T>(this IReadOnlyList<T>? list)
        {
            return list == null || list.Count == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsNullOrEmpty(this string? str)
        {
            return string.IsNullOrEmpty(str);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsInRange<T>(this IReadOnlyList<T> list, int index)
        {
            return index >= 0 && index < list.Count;
        }
    }
}
