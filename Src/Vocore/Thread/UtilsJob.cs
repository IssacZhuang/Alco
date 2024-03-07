using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vocore
{
    public static class UtilsJob
    {
        public static int InnerThreadCount => Environment.ProcessorCount * 4;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int GetOptimizedBatchCountByLength(int length)
        {
            return (length + 1023) / InnerThreadCount;
        }
    }
}

