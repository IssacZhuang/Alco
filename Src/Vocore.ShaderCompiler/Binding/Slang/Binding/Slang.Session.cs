using System.Runtime.InteropServices;

namespace SlangSharp;

public static partial class Slang
{
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangStage spReflectionEntryPoint_getStage(IntPtr entryPoint);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spReflectionEntryPoint_getName(IntPtr entryPoint);


    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spCreateSession([MarshalAs(UnmanagedType.LPStr)] string lpString);

    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spDestroySession(IntPtr session);

}