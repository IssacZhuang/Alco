using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Vocore.Unsafe
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
#if DEBUG
            IntPtr ptr = Marshal.AllocHGlobal(size);
            AllocationTracker.AddAllocated(ptr, size, Environment.StackTrace);
            return ptr.ToPointer();
#else
            return Marshal.AllocHGlobal(size).ToPointer();
#endif
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
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(T) * count);
            AllocationTracker.AddAllocated(ptr, sizeof(T) * count, Environment.StackTrace);
            return (T*)ptr.ToPointer();
#else
            return (T*)Marshal.AllocHGlobal(sizeof(T) * count).ToPointer();
#endif
        }

        /// <summary>
        /// Reallocates memory with the specified size.
        /// </summary>
        /// <param name="ptr">The pointer to the memory.</param>
        /// <param name="size">The size of the memory to allocate.</param>
        public static void* ReAlloc(void* ptr, int size)
        {
            if (ptr == null)
            {
                return Alloc(size);
            }

            Free(ptr);
            return Alloc(size);
        }

        /// <summary>
        /// Reallocates memory with the sizeof(T) * count size.
        /// </summary>
        /// <param name="ptr">The pointer to the memory.</param>
        /// <param name="count">The count of the value to allocate.</param>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <returns></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static T* ReAlloc<T>(ref void* ptr, int count) where T : unmanaged
        {
            if(ptr == null)
            {
                return Alloc<T>(count);
            }

            Free(ptr);
            return Alloc<T>(count);
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
            Marshal.FreeHGlobal((IntPtr)ptr);
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
            Marshal.FreeHGlobal(ptr);
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static IntPtr ToRef<T>(T value) where T : unmanaged
        {
            IntPtr ptr = Marshal.AllocHGlobal(sizeof(T));
#if DEBUG
            AllocationTracker.AddAllocated(ptr, sizeof(T), Environment.StackTrace);
#endif
            Marshal.StructureToPtr(value, ptr, false);
            return ptr;
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

