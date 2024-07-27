using System.Runtime.InteropServices;
using System.Text;
using SlangSharp;

internal unsafe static class UtilsSlangInterop
{
    public static string GetString(IntPtr ptr)
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
}