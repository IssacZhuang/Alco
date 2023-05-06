using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vocore
{
    public static unsafe class UtilsUnsafe
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* Alloc(int size)
        {
#if DEBUG
            IntPtr ptr = Marshal.AllocHGlobal(size);
            PointerTracker.AddAllocated(ptr, Environment.StackTrace);
            return ptr.ToPointer();
#else
            return Marshal.AllocHGlobal(size).ToPointer();
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Free(void* ptr)
        {
#if DEBUG
            PointerTracker.Remove((IntPtr)ptr);
#endif
            Marshal.FreeHGlobal((IntPtr)ptr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf(Type type)
        {
            return Marshal.SizeOf(type);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int SizeOf<T>()
        {
            return Marshal.SizeOf<T>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MemCopy(void* src, void* dest, long size)
        {
            Buffer.MemoryCopy(src, dest, size, size);
        }
    }
}

