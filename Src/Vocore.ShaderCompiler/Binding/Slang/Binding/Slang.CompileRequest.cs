using System.Runtime.InteropServices;

namespace SlangSharp;

public static partial class Slang
{

    // SLANG_API SlangCompileRequest* spCreateCompileRequest(
    //     SlangSession* session);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spCreateCompileRequest(IntPtr session);

    // SLANG_API void spDestroyCompileRequest(
    //     SlangCompileRequest* request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spDestroyCompileRequest(IntPtr request);

    // SLANG_API void spSetFileSystem(
    //     SlangCompileRequest* request,
    //     ISlangFileSystem* fileSystem);
    // Currently not implemented

    // SLANG_API void spSetCompileFlags(
    //     SlangCompileRequest* request,
    //     SlangCompileFlags flags);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetCompileFlags(IntPtr request, SlangCompileFlags flags);


    // SLANG_API SlangCompileFlags spGetCompileFlags(
    //     SlangCompileRequest* request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangCompileFlags spGetCompileFlags(IntPtr request);

    // SLANG_API void spSetDumpIntermediates(
    //     SlangCompileRequest* request,
    //     int enable);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDumpIntermediates(IntPtr request, int enable);

    // SLANG_API void spSetDumpIntermediatePrefix(
    //     SlangCompileRequest* request,
    //     const char* prefix);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDumpIntermediatePrefix(IntPtr request, [MarshalAs(UnmanagedType.LPStr)] string prefix);

    // SLANG_API void spSetLineDirectiveMode(
    //     SlangCompileRequest*    request,
    //     SlangLineDirectiveMode  mode);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetLineDirectiveMode(IntPtr request, SlangLineDirectiveMode mode);

    // SLANG_API void spSetTargetLineDirectiveMode(
    //     SlangCompileRequest* request,
    //     int targetIndex,
    //     SlangLineDirectiveMode mode);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetLineDirectiveMode(IntPtr request, int targetIndex, SlangLineDirectiveMode mode);

    // SLANG_API void spSetTargetForceGLSLScalarBufferLayout(
    //     SlangCompileRequest* request,
    //     int targetIndex,
    //     bool forceScalarLayout);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetForceGLSLScalarBufferLayout(IntPtr request, int targetIndex, bool forceScalarLayout);

    // SLANG_API void spSetCodeGenTarget(
    //     SlangCompileRequest* request,
    //     SlangCompileTarget target);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetCodeGenTarget(IntPtr request, SlangCompileTarget target);

    // SLANG_API int spAddCodeGenTarget(
    //     SlangCompileRequest* request,
    //     SlangCompileTarget target);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spAddCodeGenTarget(IntPtr request, SlangCompileTarget target);

    // SLANG_API void spSetTargetProfile(
    //     SlangCompileRequest* request,
    //     int targetIndex,
    //     SlangProfileID profile);
    // Currently not implemented

    // SLANG_API void spSetTargetFlags(
    //     SlangCompileRequest* request,
    //     int targetIndex,
    //     SlangTargetFlags flags);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetFlags(IntPtr request, int targetIndex, SlangTargetFlags flags);

    // SLANG_API void spSetTargetFloatingPointMode(
    //     SlangCompileRequest* request,
    //     int targetIndex,
    //     SlangFloatingPointMode mode);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetFloatingPointMode(IntPtr request, int targetIndex, SlangFloatingPointMode mode);

    // SLANG_API void spAddTargetCapability(
    //     slang::ICompileRequest* request,
    //     int targetIndex,
    //     SlangCapabilityID capability);
    // Currently not implemented

    // SLANG_API void spSetTargetMatrixLayoutMode(
    //     SlangCompileRequest* request,
    //     int targetIndex,
    //     SlangMatrixLayoutMode mode);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetTargetMatrixLayoutMode(IntPtr request, int targetIndex, SlangMatrixLayoutMode mode);


    // SLANG_API void spSetMatrixLayoutMode(
    //         SlangCompileRequest*    request,
    //         SlangMatrixLayoutMode   mode);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetMatrixLayoutMode(IntPtr request, SlangMatrixLayoutMode mode);

    // SLANG_API void spSetDebugInfoLevel(
    //     SlangCompileRequest* request,
    //     SlangDebugInfoLevel level);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDebugInfoLevel(IntPtr request, SlangDebugInfoLevel level);

    // SLANG_API void spSetDebugInfoFormat(
    //     SlangCompileRequest* request,
    //     SlangDebugInfoFormat format);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDebugInfoFormat(IntPtr request, SlangDebugInfoFormat format);

    // SLANG_API void spSetOptimizationLevel(
    //     SlangCompileRequest* request,
    //     SlangOptimizationLevel level);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetOptimizationLevel(IntPtr request, SlangOptimizationLevel level);

    // SLANG_API void spSetOutputContainerFormat(
    //     SlangCompileRequest* request,
    //     SlangContainerFormat format);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetOutputContainerFormat(IntPtr request, SlangContainerFormat format);

    // SLANG_API void spSetPassThrough(
    //     SlangCompileRequest* request,
    //     SlangPassThrough passThrough);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetPassThrough(IntPtr request, SlangPassThrough passThrough);

    // SLANG_API void spSetDiagnosticCallback(
    //     SlangCompileRequest* request,
    //     SlangDiagnosticCallback callback,
    //     void const* userData);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDiagnosticCallback(IntPtr request, [MarshalAs(UnmanagedType.FunctionPtr)] SlangDiagnosticCallback callback, IntPtr userData);

    // SLANG_API void spSetWriter(
    //     SlangCompileRequest*    request,
    //     SlangWriterChannel      channel, 
    //     ISlangWriter*           writer);
    // Currently not implemented

    // SLANG_API ISlangWriter* spGetWriter(
    //     SlangCompileRequest* request,
    //     SlangWriterChannel channel);
    // Currently not implemented

    // SLANG_API void spAddSearchPath(
    //     SlangCompileRequest* request,
    //     const char* searchDir);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddSearchPath(IntPtr request, [MarshalAs(UnmanagedType.LPStr)] string searchDir);

    // SLANG_API void spAddPreprocessorDefine(
    //     SlangCompileRequest* request,
    //     const char* key,
    //     const char* value);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddPreprocessorDefine(IntPtr request, [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string val);

    // SLANG_API SlangResult spProcessCommandLineArguments(
    //     SlangCompileRequest* request,
    //     char const* const* args,
    //     int argCount);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spProcessCommandLineArguments(IntPtr request, [In] string[] args, int argCount);

    // SLANG_API int spAddTranslationUnit(
    //     SlangCompileRequest* request,
    //     SlangSourceLanguage language,
    //     char const* name);

    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spAddTranslationUnit(IntPtr request, SlangSourceLanguage language, [MarshalAs(UnmanagedType.LPStr)] string name);

    // SLANG_API void spSetDefaultModuleName(
    //     SlangCompileRequest* request,
    //     const char* defaultModuleName);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDefaultModuleName(IntPtr request, [MarshalAs(UnmanagedType.LPStr)] string defaultModuleName);

    // SLANG_API void spTranslationUnit_addPreprocessorDefine(
    //     SlangCompileRequest* request,
    //     int translationUnitIndex,
    //     const char* key,
    //     const char* value);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spTranslationUnit_addPreprocessorDefine(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string key, [MarshalAs(UnmanagedType.LPStr)] string value);

    // SLANG_API void spAddTranslationUnitSourceFile(
    //     SlangCompileRequest* request,
    //     int translationUnitIndex,
    //     char const* path);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddTranslationUnitSourceFile(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string path);

    // SLANG_API void spAddTranslationUnitSourceString(
    //     SlangCompileRequest* request,
    //     int translationUnitIndex,
    //     char const* path,
    //     char const* source);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddTranslationUnitSourceString(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string path, [MarshalAs(UnmanagedType.LPStr)] string source);

    // SLANG_API SlangResult spAddLibraryReference(
    //     SlangCompileRequest* request,
    //     const char* basePath,
    //     const void* libData,
    //     size_t libDataSize);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spAddLibraryReference(IntPtr request, [MarshalAs(UnmanagedType.LPStr)] string basePath, IntPtr libData, nuint libDataSize);

    // SLANG_API void spAddTranslationUnitSourceStringSpan(
    //     SlangCompileRequest* request,
    //     int translationUnitIndex,
    //     char const* path,
    //     char const* sourceBegin,
    //     char const* sourceEnd);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spAddTranslationUnitSourceStringSpan(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string path, [MarshalAs(UnmanagedType.LPStr)] string sourceBegin, [MarshalAs(UnmanagedType.LPStr)] string sourceEnd);

    // SLANG_API void spAddTranslationUnitSourceBlob(
    //     SlangCompileRequest* request,
    //     int translationUnitIndex,
    //     char const* path,
    //     ISlangBlob*             sourceBlob);
    // Currently not implemented

    // SLANG_API int spAddEntryPoint(
    //     SlangCompileRequest* request,
    //     int translationUnitIndex,
    //     char const* name,
    //     SlangStage              stage);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spAddEntryPoint(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string name, SlangStage stage);

    // SLANG_API int spAddEntryPointEx(
    //      SlangCompileRequest* request,
    //      int translationUnitIndex,
    //      char const* name,
    //      SlangStage              stage,
    //     int genericArgCount,
    //     char const** genericArgs);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spAddEntryPointEx(IntPtr request, int translationUnitIndex, [MarshalAs(UnmanagedType.LPStr)] string name, SlangStage stage, int genericArgCount, [In] string[] genericArgs);

    // SLANG_API SlangResult spSetGlobalGenericArgs(
    //     SlangCompileRequest* request,
    //     int genericArgCount,
    //     char const** genericArgs);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spSetGlobalGenericArgs(IntPtr request, int genericArgCount, [In] string[] genericArgs);

    // SLANG_API SlangResult spSetTypeNameForGlobalExistentialTypeParam(
    //     SlangCompileRequest* request,
    //     int slotIndex,
    //     char const* typeName);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spSetTypeNameForGlobalExistentialTypeParam(IntPtr request, int slotIndex, [MarshalAs(UnmanagedType.LPStr)] string typeName);

    // SLANG_API SlangResult spSetTypeNameForEntryPointExistentialTypeParam(
    //     SlangCompileRequest* request,
    //     int entryPointIndex,
    //     int slotIndex,
    //     char const* typeName);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spSetTypeNameForEntryPointExistentialTypeParam(IntPtr request, int entryPointIndex, int slotIndex, [MarshalAs(UnmanagedType.LPStr)] string typeName);

    // SLANG_API SlangResult spCompile(
    //     SlangCompileRequest* request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spCompile(IntPtr request);

    // SLANG_API char const* spGetDiagnosticOutput(
    //     SlangCompileRequest * request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spGetDiagnosticOutput(IntPtr request);

    // SLANG_API int
    // spGetDependencyFileCount(
    //     SlangCompileRequest* request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangResult spGetDependencyFileCount(IntPtr request);

    // SLANG_API char const*
    // spGetDependencyFilePath(
    //     SlangCompileRequest* request,
    //     int index);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spGetDependencyFilePath(IntPtr request, int index);

    // SLANG_API int
    // spGetTranslationUnitCount(
    //     SlangCompileRequest* request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern int spGetTranslationUnitCount(IntPtr request);

    // SLANG_API char const* spGetEntryPointSource(
    //     SlangCompileRequest * request,
    //     int                     entryPointIndex);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spGetEntryPointSource(IntPtr request, int entryPointIndex);

    // SLANG_API void const* spGetEntryPointCode(
    //     SlangCompileRequest * request,
    //     int                     entryPointIndex,
    //     size_t * outSize);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spGetEntryPointCode(IntPtr request, int entryPointIndex, [Out] out nuint outSize);

    // SLANG_API SlangResult spGetEntryPointCodeBlob(
    //     SlangCompileRequest* request,
    //     int entryPointIndex,
    //     int targetIndex,
    //     ISlangBlob** outBlob);
    // Currently not implemented

    // SLANG_API SlangResult spGetEntryPointHostCallable(
    //     SlangCompileRequest* request,
    //     int entryPointIndex,
    //     int targetIndex,
    //     ISlangSharedLibrary** outSharedLibrary);
    // Currently not implemented

    // SLANG_API SlangResult spGetTargetCodeBlob(
    //     SlangCompileRequest* request,
    //     int targetIndex,
    //     ISlangBlob** outBlob);
    // Currently not implemented

    // SLANG_API SlangResult spGetTargetHostCallable(
    //     SlangCompileRequest* request,
    //     int targetIndex,
    //     ISlangSharedLibrary** outSharedLibrary);
    // Currently not implemented

    // SLANG_API void const* spGetCompileRequestCode(
    //     SlangCompileRequest * request,
    //         size_t * outSize);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern IntPtr spGetCompileRequestCode(IntPtr request, [Out] out nuint size);

    // SLANG_API SlangResult spGetContainerCode(
    //     SlangCompileRequest* request,
    //     ISlangBlob** outBlob);
    // Currently not implemented

    // SLANG_API SlangResult spLoadRepro(
    //     SlangCompileRequest* request,
    //     ISlangFileSystem* fileSystem,
    //     const void* data,
    //     size_t size);
    // Currently not implemented

    // SLANG_API SlangResult spSaveRepro(
    //     SlangCompileRequest* request,
    //     ISlangBlob** outBlob
    // );
    // Currently not implemented

    // SLANG_API SlangResult spEnableReproCapture(
    //     SlangCompileRequest* request);
    // Currently not implemented


    // SLANG_API void spOverrideDiagnosticSeverity(
    //     SlangCompileRequest* request,
    //     SlangInt messageID,
    //     SlangSeverity overrideSeverity);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spOverrideDiagnosticSeverity(IntPtr request, int messageID, SlangSeverity overrideSeverity);

    // SLANG_API SlangDiagnosticFlags spGetDiagnosticFlags(SlangCompileRequest* request);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern SlangDiagnosticFlags spGetDiagnosticFlags(IntPtr request);

    // SLANG_API void spSetDiagnosticFlags(SlangCompileRequest* request, SlangDiagnosticFlags flags);
    [DllImport("slang", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    public static extern void spSetDiagnosticFlags(IntPtr request, SlangDiagnosticFlags flags);


    public delegate void SlangDiagnosticCallback(IntPtr message, IntPtr userData);






}