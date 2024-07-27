using System.Runtime.InteropServices;

namespace SlangSharp;

public static partial class Slang
{

    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spGetReflection(IntPtr request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern uint spReflection_getEntryPointCount(IntPtr reflection);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spReflection_getEntryPointByIndex(IntPtr reflection, uint index);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spReflection_findEntryPointByName(IntPtr reflection, [MarshalAs(UnmanagedType.LPStr)] string name);



}