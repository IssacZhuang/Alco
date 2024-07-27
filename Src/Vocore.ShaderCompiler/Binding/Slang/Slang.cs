using System.Runtime.InteropServices;

namespace SlangSharp;

public static partial class Slang
{

    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spCreateCompileRequest(IntPtr session);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spDestroyCompileRequest(IntPtr request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetCodeGenTarget(IntPtr request, SlangCompileTarget target);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetMatrixLayoutMode(IntPtr request, SlangMatrixLayoutMode mode);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetFlags(IntPtr request, int targetIndex, SlangTargetFlags flags);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetLineDirectiveMode(IntPtr request, int targetIndex, SlangLineDirectiveMode mode);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spAddTranslationUnit(IntPtr request, SlangSourceLanguage language, [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddTranslationUnitSourceFile(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string path);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddTranslationUnitSourceString(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string path, [MarshalAs(UnmanagedType.LPStr)] string source);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spAddEntryPoint(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string name, SlangStage stage);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddSearchPath(IntPtr request, [MarshalAs(UnmanagedType.LPStr)] string searchDir);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddPreprocessorDefine(IntPtr request, [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string val);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spProcessCommandLineArguments(IntPtr request, [In] string[] args, int argCount);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spCompile(IntPtr request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spGetCompileRequestCode(IntPtr request, [Out] out nuint size);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spGetDiagnosticOutput(IntPtr request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spGetDependencyFileCount(IntPtr request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spGetDependencyFilePath(IntPtr request, int index);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spOverrideDiagnosticSeverity(IntPtr request, int messageID, SlangSeverity overrideSeverity);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDiagnosticCallback(IntPtr request, [MarshalAs(UnmanagedType.FunctionPtr)] SlangDiagnosticCallback callback, IntPtr userData);

    public delegate void SlangDiagnosticCallback(IntPtr message, IntPtr userData);



    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spGetReflection(IntPtr request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern uint spReflection_getEntryPointCount(IntPtr reflection);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spReflection_getEntryPointByIndex(IntPtr reflection, uint index);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spReflection_findEntryPointByName(IntPtr reflection, [MarshalAs(UnmanagedType.LPStr)] string name);



    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangStage spReflectionEntryPoint_getStage(IntPtr entryPoint);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spReflectionEntryPoint_getName(IntPtr entryPoint);


    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spCreateSession([MarshalAs(UnmanagedType.LPStr)] string lpString);

    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spDestroySession(IntPtr session);
}