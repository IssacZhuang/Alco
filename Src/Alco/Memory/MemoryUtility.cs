using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco
{
    public static unsafe class MemoryUtility
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

        /// <summary>
        /// Allocates the given number of bytes from native memory.
        /// </summary>
        /// <param name="size">The number of bytes to allocate.</param>
        /// <returns>A pointer to the allocated memory.</returns>
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

        /// <summary>
        /// Allocates native memory for the given number of unmanaged elements.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="count">The number of elements to allocate.</param>
        /// <returns>A pointer to the allocated memory.</returns>
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


        /// <summary>
        /// Fills the given number of bytes starting at the pointer with a byte value.
        /// </summary>
        /// <param name="ptr">The memory to fill.</param>
        /// <param name="size">The number of bytes to fill.</param>
        /// <param name="value">The byte value to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Memset(void* ptr, long size, byte value)
        {
            byte* ptr2 = (byte*)ptr;
            for (long i = 0; i < size; i++)
            {
                ptr2[i] = value;
            }
        }

        /// <summary>
        /// Fills the given number of elements starting at the pointer with a value.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="ptr">The memory to fill.</param>
        /// <param name="size">The number of elements to fill.</param>
        /// <param name="value">The value to write.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Memset<T>(T* ptr, long size, T value) where T : unmanaged
        {
            for (long i = 0; i < size; i++)
            {
                ptr[i] = value;
            }
        }

        /// <summary>
        /// Copies the given number of bytes from the source to the destination.
        /// </summary>
        /// <param name="src">The source memory.</param>
        /// <param name="dest">The destination memory.</param>
        /// <param name="size">The number of bytes to copy.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MemCopy(void* src, void* dest, long size)
        {
            System.Runtime.CompilerServices.Unsafe.CopyBlock(dest, src, (uint)size);
        }

        /// <summary>
        /// Copies the given number of bytes from the source to the destination.
        /// </summary>
        /// <param name="src">The source memory.</param>
        /// <param name="dest">The destination memory.</param>
        /// <param name="size">The number of bytes to copy.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void MemCopy(void* src, void* dest, uint size)
        {
            System.Runtime.CompilerServices.Unsafe.CopyBlock(dest, src, size);
        }

    }
}

