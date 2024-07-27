using System.Runtime.InteropServices;

namespace SlangSharp;

public static partial class Slang
{

    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflection spGetReflection(SlangCompileRequest request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern uint spReflection_getEntryPointCount(SlangReflection request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionEntryPoint spReflection_getEntryPointByIndex(SlangReflection request, uint index);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangReflectionEntryPoint spReflection_findEntryPointByName(SlangReflection request, [MarshalAs(UnmanagedType.LPStr)] string name);


    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangStage spReflectionEntryPoint_getStage(SlangReflectionEntryPoint entryPoint);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spReflectionEntryPoint_getName(SlangReflectionEntryPoint entryPoint);


}