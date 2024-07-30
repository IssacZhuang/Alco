using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Core.Native;
using SlangSharp;

namespace SlangSharp;

internal unsafe static class UtilsSlangInterop
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

    public static void Copy(void* src, void* dst, nuint srcSize, nuint dstSize)
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


    public static string GetStringUtf8(void* ptr)
    {
        Span<byte> span = new Span<byte>(ptr, int.MaxValue);
        span = span.Slice(0, span.IndexOf<byte>(0));
        if (span.Length == 0)
        {
            return string.Empty;
        }

        fixed (byte* bytes = span)
        {
            return Encoding.UTF8.GetString(bytes, span.Length);
        }
    }


    public static string GetStringAnsi(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
        {
            return string.Empty;
        }

        return Marshal.PtrToStringAnsi(ptr)!;
    }

    public static string GetString(IntPtr ptr, nuint size)
    {
        if (ptr == IntPtr.Zero)
        {
            return string.Empty;
        }

        byte* buffer = (byte*)ptr;

        return Encoding.UTF8.GetString(buffer, (int)size);
    }

    public static byte[] GetBytes(IntPtr ptr, nuint size)
    {
        if (ptr == IntPtr.Zero)
        {
            return Array.Empty<byte>();
        }

        byte* buffer = (byte*)ptr;

        byte[] data = new byte[size];
        Marshal.Copy(ptr, data, 0, (int)size);

        return data;
    }
}