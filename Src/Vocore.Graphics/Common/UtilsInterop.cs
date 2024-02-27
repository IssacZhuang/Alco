using System.Runtime.InteropServices;
using Silk.NET.Core.Native;

namespace Vocore.Graphics;

internal unsafe static class UtilsInterop
{

    public static T* Alloc<T>(int count) where T : unmanaged
    {
        return (T*)Marshal.AllocHGlobal(sizeof(T) * count);
    }

    public static void Free(void* ptr)
    {
        Marshal.FreeHGlobal((IntPtr)ptr);
    }

    public static void Copy(void* src, void* dst, uint srcSize, uint dstSize)
    {
        Buffer.MemoryCopy(src, dst, srcSize, dstSize);
    }

    public static void Memset<T>(T* src, int length, T value) where T : unmanaged
    {
        for (int i = 0; i < length; i++)
        {
            src[i] = value;
        }
    }

    public static unsafe T[] ReadNativeArray<T>(T* ptr, uint count) where T : unmanaged
    {
        T[] array = new T[count];
        for (int i = 0; i < count; i++)
        {
            array[i] = ptr[i];
        }
        return array;
    }

    public unsafe static string ReadString(byte* ptrString)
    {
        return SilkMarshal.PtrToString((IntPtr)ptrString) ?? string.Empty;
    }
}