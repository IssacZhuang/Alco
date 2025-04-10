using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco
{
    public static unsafe class UtilsMemory
    {
        /// <summary>
        /// Allocates memory with the specified size.
        /// </summary>
        /// <param name="size">The size of the memory to allocate.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* Alloc(int size)
        {
            void* ptr = NativeMemory.Alloc((nuint)size);
#if DEBUG
            AllocationTracker.AddAllocated((nint)ptr, size, Environment.StackTrace);
#endif
            return ptr;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void* Alloc(long size)
        {
            void* ptr = NativeMemory.Alloc((nuint)size);
#if DEBUG
            AllocationTracker.AddAllocated((nint)ptr, size, Environment.StackTrace);
#endif
            return ptr;
        }

        /// <summary>
        /// Allocates memory with the sizeof(T) * count size.
        /// </summary>
        /// <param name="count">The count of the value to allocate.</param>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T* Alloc<T>(int count) where T : unmanaged
        {
#if DEBUG
            void* ptr = NativeMemory.Alloc((nuint)(sizeof(T) * count));
            AllocationTracker.AddAllocated((nint)ptr, sizeof(T) * count, Environment.StackTrace);
            return (T*)ptr;
#else
            return (T*)NativeMemory.Alloc((nuint)(sizeof(T) * count));
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T* Alloc<T>(long count) where T : unmanaged
        {
            void* ptr = NativeMemory.Alloc((nuint)(sizeof(T) * count));
#if DEBUG
            AllocationTracker.AddAllocated((nint)ptr, sizeof(T) * count, Environment.StackTrace);
#endif
            return (T*)ptr;
        }


        /// <summary>
        /// Frees the memory.
        /// </summary>
        /// <param name="ptr">The pointer to the memory.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Free(void* ptr)
        {
#if DEBUG
            AllocationTracker.Remove((IntPtr)ptr);
#endif
            NativeMemory.Free(ptr);
        }

        /// <summary>
        /// Frees the memory.
        /// </summary>
        /// <param name="ptr">The pointer to the memory.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Free(IntPtr ptr)
        {
#if DEBUG
            AllocationTracker.Remove(ptr);
#endif
            NativeMemory.Free((void*)ptr);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Memset(void* ptr, long size, byte value)
        {
            byte* ptr2 = (byte*)ptr;
            for (long i = 0; i < size; i++)
            {
                ptr2[i] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Memset<T>(T* ptr, long size, T value) where T : unmanaged
        {
            for (long i = 0; i < size; i++)
            {
                ptr[i] = value;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MemCopy(void* src, void* dest, long size)
        {
            System.Runtime.CompilerServices.Unsafe.CopyBlock(dest, src, (uint)size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MemCopy(void* src, void* dest, uint size)
        {
            System.Runtime.CompilerServices.Unsafe.CopyBlock(dest, src, size);
        }

    }
}

