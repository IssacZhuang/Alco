using System.Runtime.InteropServices;
using System.Text;

namespace Alco.Graphics;

internal unsafe static class InteropUtility
{

#if DEBUG
    private static long _outstandingAllocationCount;
    internal static long OutstandingAllocationCount => Interlocked.Read(ref _outstandingAllocationCount);
#endif

    /// <summary>Allocates a native array that must be released with <see cref="Free"/>.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="count">The number of elements to allocate.</param>
    /// <returns>The native array pointer.</returns>
    public static T* Alloc<T>(int count) where T : unmanaged
    {
        T* ptr = (T*)Marshal.AllocHGlobal(sizeof(T) * count);
#if DEBUG
        if (ptr != null)
        {
            Interlocked.Increment(ref _outstandingAllocationCount);
        }
#endif
        return ptr;
    }

    /// <summary>Releases an array allocated by <see cref="Alloc{T}"/>.</summary>
    /// <param name="ptr">The array pointer, or null.</param>
    public static void Free(void* ptr)
    {
        Marshal.FreeHGlobal((IntPtr)ptr);
#if DEBUG
        if (ptr != null)
        {
            Interlocked.Decrement(ref _outstandingAllocationCount);
        }
#endif
    }

    /// <summary>Copies bytes between native memory regions.</summary>
    /// <param name="src">The source pointer.</param>
    /// <param name="dst">The destination pointer.</param>
    /// <param name="srcSize">The number of bytes to copy.</param>
    /// <param name="dstSize">The destination capacity in bytes.</param>
    public static void Copy(void* src, void* dst, uint srcSize, uint dstSize)
    {
        Buffer.MemoryCopy(src, dst, srcSize, dstSize);
    }

    /// <summary>Fills a native array with the supplied value.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="src">The array pointer.</param>
    /// <param name="length">The number of elements to fill.</param>
    /// <param name="value">The value assigned to each element.</param>
    public static void Memset<T>(T* src, int length, T value) where T : unmanaged
    {
        for (int i = 0; i < length; i++)
        {
            src[i] = value;
        }
    }

    /// <summary>Copies a native array into a new managed array.</summary>
    /// <typeparam name="T">The unmanaged element type.</typeparam>
    /// <param name="ptr">The source array pointer.</param>
    /// <param name="count">The number of elements to copy.</param>
    /// <returns>The managed copy of the native elements.</returns>
    public static unsafe T[] ReadNativeArray<T>(T* ptr, uint count) where T : unmanaged
    {
        T[] array = new T[count];
        for (int i = 0; i < count; i++)
        {
            array[i] = ptr[i];
        }
        return array;
    }

    /// <summary>Reads a NUL-terminated UTF-8 string from native memory.</summary>
    /// <param name="ptrString">The string pointer, or null for an empty string.</param>
    /// <returns>The decoded managed string.</returns>
    public unsafe static string ReadString(byte* ptrString)
    {
        if (ptrString == null)
        {
            return string.Empty;
        }

        int length = 0;
        while (ptrString[length] != 0)
        {
            length++;
        }

        return Encoding.UTF8.GetString(ptrString, length);
    }
}