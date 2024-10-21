using System.Runtime.InteropServices;

namespace Vocore.Audio;

internal unsafe static class Allocator
{
    public static void* Alloc(int size)
    {
        return Marshal.AllocHGlobal(size).ToPointer();
    }

    public static void Free(void* ptr)
    {
        Marshal.FreeHGlobal((IntPtr)ptr);
    }
}