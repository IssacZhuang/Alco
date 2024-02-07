using System.Runtime.InteropServices;

namespace Vocore.Graphics;

public unsafe static class UtilsInterop
{

    public static T* Alloc<T>(int count) where T : unmanaged
    {
        return (T*)Marshal.AllocHGlobal(sizeof(T) * count);
    }

    public static void Free<T>(T* ptr) where T : unmanaged
    {
        Marshal.FreeHGlobal((IntPtr)ptr);
    }

    public static void Copy<T>(T* src, T* dst, uint srcSize, uint dstSize) where T : unmanaged
    {
        Buffer.MemoryCopy(src, dst, srcSize, dstSize);
    }
}