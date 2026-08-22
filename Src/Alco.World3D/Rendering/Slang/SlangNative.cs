using System.Runtime.InteropServices;

namespace Alco.World3D;

/// <summary>
/// Raw P/Invoke surface over the Slang shader compiler's flat C API
/// (slang-deprecated.h of the slang SDK - despite the header name it is the
/// live C ABI the C++ wrappers call). The engine's own DXC + SPIR-V-reflect
/// toolchain is untouched; this binding serves World3D's Slang material and
/// pipeline shader paths. Function order mirrors the header for reviewability.
/// </summary>
internal static class SlangNative
{
    private const string Slang = "slang";

    // ── SlangCompileTarget ──
    public const int SLANG_SPIRV = 6;

    // ── SlangOptimizationLevel ──
    public const int SLANG_OPTIMIZATION_LEVEL_MAXIMAL = 3;

    // ── SlangMatrixLayoutMode ──
    public const int SLANG_MATRIX_LAYOUT_COLUMN_MAJOR = 2;

    // ── SlangSourceLanguage ──
    public const int SLANG_SOURCE_LANGUAGE_SLANG = 1;

    // ── SlangStage ──
    public const int SLANG_STAGE_NONE = 0;
    public const int SLANG_STAGE_VERTEX = 1;
    public const int SLANG_STAGE_FRAGMENT = 5;
    public const int SLANG_STAGE_COMPUTE = 6;

    // ── SlangTypeKind ──
    public const int SLANG_TYPE_KIND_NONE = 0;
    public const int SLANG_TYPE_KIND_STRUCT = 1;
    public const int SLANG_TYPE_KIND_ARRAY = 2;
    public const int SLANG_TYPE_KIND_MATRIX = 3;
    public const int SLANG_TYPE_KIND_VECTOR = 4;
    public const int SLANG_TYPE_KIND_SCALAR = 5;
    public const int SLANG_TYPE_KIND_CONSTANT_BUFFER = 6;
    public const int SLANG_TYPE_KIND_RESOURCE = 7;
    public const int SLANG_TYPE_KIND_SAMPLER_STATE = 8;
    public const int SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER = 10;

    // ── SlangParameterCategory ──
    public const int SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER = 2;
    public const int SLANG_PARAMETER_CATEGORY_VARYING_INPUT = 5;
    public const int SLANG_PARAMETER_CATEGORY_VARYING_OUTPUT = 6;
    public const int SLANG_PARAMETER_CATEGORY_SAMPLER_STATE = 7;
    public const int SLANG_PARAMETER_CATEGORY_UNIFORM = 8;
    public const int SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT = 9;
    public const int SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER = 11;

    // ── SlangResourceShape (base shapes; array flag 0x40) ──
    public const int SLANG_TEXTURE_1D = 0x01;
    public const int SLANG_TEXTURE_2D = 0x02;
    public const int SLANG_TEXTURE_3D = 0x03;
    public const int SLANG_TEXTURE_CUBE = 0x04;
    public const int SLANG_STRUCTURED_BUFFER = 0x06;
    public const int SLANG_TEXTURE_ARRAY_FLAG = 0x40;

    // ── SlangResourceAccess ──
    public const int SLANG_RESOURCE_ACCESS_NONE = 0;
    public const int SLANG_RESOURCE_ACCESS_READ = 1;
    public const int SLANG_RESOURCE_ACCESS_READ_WRITE = 2;
    public const int SLANG_RESOURCE_ACCESS_WRITE = 6;

    // ── SlangScalarType ──
    public const int SLANG_SCALAR_TYPE_NONE = 0;
    public const int SLANG_SCALAR_TYPE_BOOL = 2;
    public const int SLANG_SCALAR_TYPE_INT32 = 3;
    public const int SLANG_SCALAR_TYPE_UINT32 = 4;
    public const int SLANG_SCALAR_TYPE_FLOAT32 = 8;

    /// <summary>SlangResult success value (SLANG_OK).</summary>
    public const int SLANG_OK = 0;

    // ── session / compile request ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spCreateSession(string? deprecated = null);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spDestroySession(IntPtr session);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spCreateCompileRequest(IntPtr session);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spDestroyCompileRequest(IntPtr request);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spSetFileSystem(IntPtr request, IntPtr fileSystem);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spSetCodeGenTarget(IntPtr request, int target);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spSetOptimizationLevel(IntPtr request, int level);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spSetMatrixLayoutMode(IntPtr request, int mode);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spProcessCommandLineArguments(
        IntPtr request,
        IntPtr[] arguments,
        int argumentCount);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spAddTranslationUnit(IntPtr request, int language, string name);

    // The source must be UTF-8 bytes (Slang parses UTF-8; the default ANSI
    // string marshalling would corrupt non-ASCII characters in comments).
    // The caller appends the NUL terminator.
    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spAddTranslationUnitSourceString(IntPtr request, int translationUnitIndex, string path, byte[] source);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spTranslationUnit_addPreprocessorDefine(IntPtr request, int translationUnitIndex, string name, string value);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spAddEntryPoint(IntPtr request, int translationUnitIndex, string name, int stage);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spCompile(IntPtr request);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spGetDiagnosticOutput(IntPtr request);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spGetEntryPointCode(IntPtr request, int entryPointIndex, out nuint outSize);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spGetReflection(IntPtr request);

    // ── blob factory for the file system callback ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr slang_createBlob(byte[] data, nuint size);

    // ── reflection: program parameters ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflection_GetParameterCount(IntPtr reflection);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflection_GetParameterByIndex(IntPtr reflection, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint spReflection_getEntryPointCount(IntPtr reflection);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflection_getEntryPointByIndex(IntPtr reflection, nuint index);

    // ── reflection: variables and layouts ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionVariableLayout_GetVariable(IntPtr variableLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionVariableLayout_GetTypeLayout(IntPtr variableLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint spReflectionVariableLayout_GetOffset(IntPtr variableLayout, int category);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionVariableLayout_GetSemanticName(IntPtr variableLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionVariable_GetName(IntPtr variable);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionVariable_GetType(IntPtr variable);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionParameter_GetBindingIndex(IntPtr parameter);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionParameter_GetBindingSpace(IntPtr parameter);

    // ── reflection: type layouts ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionTypeLayout_GetType(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionTypeLayout_getKind(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint spReflectionTypeLayout_GetSize(IntPtr typeLayout, int category);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionTypeLayout_GetElementTypeLayout(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionTypeLayout_GetFieldCount(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionTypeLayout_GetFieldByIndex(IntPtr typeLayout, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionTypeLayout_GetParameterCategory(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionTypeLayout_GetCategoryCount(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionTypeLayout_GetCategoryByIndex(IntPtr typeLayout, uint index);

    // ── reflection: types ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionType_GetKind(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionType_GetResourceShape(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionType_GetResourceAccess(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionType_GetRowCount(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionType_GetColumnCount(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionType_GetScalarType(IntPtr type);

    // ── reflection: entry points ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionEntryPoint_getStage(IntPtr entryPoint);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionEntryPoint_getParameterCount(IntPtr entryPoint);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionEntryPoint_getParameterByIndex(IntPtr entryPoint, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionEntryPoint_getResultVarLayout(IntPtr entryPoint);

    /// <summary>Marshal a slang UTF-8 char* return value; null-safe.</summary>
    public static string? StringFromPtr(IntPtr ptr)
    {
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }
}
