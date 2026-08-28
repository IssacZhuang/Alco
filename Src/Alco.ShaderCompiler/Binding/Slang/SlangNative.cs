using System.Runtime.InteropServices;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Pinned slang release: 2026.16 (slang.dll / libslang.so / libslang.dylib).
//
// This file is the raw P/Invoke surface over slang's C exports: the process
// entry points (global session, blob factory) and the reflection query
// functions (spReflection*). The compile path uses the modern COM-style
// interfaces (see SlangCom.cs); the reflection path deliberately stays on the
// flat C query functions because the C++ reflection types in slang.h are
// inline wrappers over exactly these calls — they are the sanctioned C ABI,
// not the deprecated ICompileRequest API.
//
// Layout mirrors slang.h / slang-deprecated.h of the pinned version.
// ─────────────────────────────────────────────────────────────────────────────

internal static class SlangNative
{
    private const string Slang = "slang";

    // ── SlangResult ──
    public const int SLANG_OK = 0;

    // ── SlangCompileTarget ──
    public const int SLANG_TARGET_NONE = 1;
    public const int SLANG_SPIRV = 6;
    public const int SLANG_DXIL = 10;
    public const int SLANG_METAL = 24;
    public const int SLANG_METAL_LIB = 25;

    // ── SlangProfileID ──
    public const int SLANG_PROFILE_UNKNOWN = 0;

    // ── SlangStage ──
    public const int SLANG_STAGE_NONE = 0;
    public const int SLANG_STAGE_VERTEX = 1;
    public const int SLANG_STAGE_HULL = 2;
    public const int SLANG_STAGE_DOMAIN = 3;
    public const int SLANG_STAGE_GEOMETRY = 4;
    public const int SLANG_STAGE_FRAGMENT = 5;
    public const int SLANG_STAGE_COMPUTE = 6;

    // ── SlangMatrixLayoutMode ──
    public const int SLANG_MATRIX_LAYOUT_ROW_MAJOR = 1;
    public const int SLANG_MATRIX_LAYOUT_COLUMN_MAJOR = 2;

    // ── SlangOptimizationLevel ──
    public const int SLANG_OPTIMIZATION_LEVEL_NONE = 0;
    public const int SLANG_OPTIMIZATION_LEVEL_DEFAULT = 1;
    public const int SLANG_OPTIMIZATION_LEVEL_HIGH = 2;
    public const int SLANG_OPTIMIZATION_LEVEL_MAXIMAL = 3;

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
    public const int SLANG_TYPE_KIND_TEXTURE_BUFFER = 9;
    public const int SLANG_TYPE_KIND_SHADER_STORAGE_BUFFER = 10;
    public const int SLANG_TYPE_KIND_PARAMETER_BLOCK = 11;
    public const int SLANG_TYPE_KIND_GENERIC_TYPE_PARAMETER = 12;
    public const int SLANG_TYPE_KIND_INTERFACE = 13;
    public const int SLANG_TYPE_KIND_OUTPUT_STREAM = 14;
    public const int SLANG_TYPE_KIND_MESH_OUTPUT = 15;
    public const int SLANG_TYPE_KIND_SPECIALIZED = 16;
    public const int SLANG_TYPE_KIND_FEEDBACK = 17;
    public const int SLANG_TYPE_KIND_POINTER = 18;
    public const int SLANG_TYPE_KIND_DYNAMIC_RESOURCE = 19;
    public const int SLANG_TYPE_KIND_ENUM = 20;

    // ── SlangBindingType (descriptor kinds; MUTABLE flag = read-write access) ──
    public const uint SLANG_BINDING_TYPE_UNKNOWN = 0;
    public const uint SLANG_BINDING_TYPE_SAMPLER = 1;
    public const uint SLANG_BINDING_TYPE_TEXTURE = 2;
    public const uint SLANG_BINDING_TYPE_CONSTANT_BUFFER = 3;
    public const uint SLANG_BINDING_TYPE_PARAMETER_BLOCK = 4;
    public const uint SLANG_BINDING_TYPE_TYPED_BUFFER = 5;
    public const uint SLANG_BINDING_TYPE_RAW_BUFFER = 6;
    public const uint SLANG_BINDING_TYPE_COMBINED_TEXTURE_SAMPLER = 7;
    public const uint SLANG_BINDING_TYPE_INPUT_RENDER_TARGET = 8;
    public const uint SLANG_BINDING_TYPE_INLINE_UNIFORM_DATA = 9;
    public const uint SLANG_BINDING_TYPE_RAY_TRACING_ACCELERATION_STRUCTURE = 10;
    public const uint SLANG_BINDING_TYPE_VARYING_INPUT = 11;
    public const uint SLANG_BINDING_TYPE_VARYING_OUTPUT = 12;
    public const uint SLANG_BINDING_TYPE_EXISTENTIAL_VALUE = 13;
    public const uint SLANG_BINDING_TYPE_PUSH_CONSTANT = 14;
    public const uint SLANG_BINDING_TYPE_MUTABLE_FLAG = 0x100;
    public const uint SLANG_BINDING_TYPE_BASE_MASK = 0x00FF;

    // ── SlangParameterCategory (layout spaces) ──
    public const int SLANG_PARAMETER_CATEGORY_NONE = 0;
    public const int SLANG_PARAMETER_CATEGORY_MIXED = 1;
    public const int SLANG_PARAMETER_CATEGORY_CONSTANT_BUFFER = 2;
    public const int SLANG_PARAMETER_CATEGORY_SHADER_RESOURCE = 3;
    public const int SLANG_PARAMETER_CATEGORY_UNORDERED_ACCESS = 4;
    public const int SLANG_PARAMETER_CATEGORY_VARYING_INPUT = 5;
    public const int SLANG_PARAMETER_CATEGORY_VARYING_OUTPUT = 6;
    public const int SLANG_PARAMETER_CATEGORY_SAMPLER_STATE = 7;
    public const int SLANG_PARAMETER_CATEGORY_UNIFORM = 8;
    public const int SLANG_PARAMETER_CATEGORY_DESCRIPTOR_TABLE_SLOT = 9;
    public const int SLANG_PARAMETER_CATEGORY_SPECIALIZATION_CONSTANT = 10;
    public const int SLANG_PARAMETER_CATEGORY_PUSH_CONSTANT_BUFFER = 11;
    public const int SLANG_PARAMETER_CATEGORY_REGISTER_SPACE = 12;
    public const int SLANG_PARAMETER_CATEGORY_GENERIC = 13;

    // ── SlangResourceShape (base shapes; array flag 0x40) ──
    public const int SLANG_RESOURCE_NONE = 0x00;
    public const int SLANG_TEXTURE_1D = 0x01;
    public const int SLANG_TEXTURE_2D = 0x02;
    public const int SLANG_TEXTURE_3D = 0x03;
    public const int SLANG_TEXTURE_CUBE = 0x04;
    public const int SLANG_TEXTURE_BUFFER = 0x05;
    public const int SLANG_STRUCTURED_BUFFER = 0x06;
    public const int SLANG_BYTE_ADDRESS_BUFFER = 0x07;
    public const int SLANG_TEXTURE_SHADOW_FLAG = 0x20;
    public const int SLANG_TEXTURE_ARRAY_FLAG = 0x40;
    public const int SLANG_TEXTURE_MULTISAMPLE_FLAG = 0x80;

    // ── SlangResourceAccess ──
    public const int SLANG_RESOURCE_ACCESS_NONE = 0;
    public const int SLANG_RESOURCE_ACCESS_READ = 1;
    public const int SLANG_RESOURCE_ACCESS_READ_WRITE = 2;
    public const int SLANG_RESOURCE_ACCESS_RASTER_ORDERED = 3;
    public const int SLANG_RESOURCE_ACCESS_APPEND = 4;
    public const int SLANG_RESOURCE_ACCESS_CONSUME = 5;
    public const int SLANG_RESOURCE_ACCESS_WRITE = 6;
    public const int SLANG_RESOURCE_ACCESS_FEEDBACK = 7;

    // ── SlangScalarType ──
    public const int SLANG_SCALAR_TYPE_NONE = 0;
    public const int SLANG_SCALAR_TYPE_VOID = 1;
    public const int SLANG_SCALAR_TYPE_BOOL = 2;
    public const int SLANG_SCALAR_TYPE_INT32 = 3;
    public const int SLANG_SCALAR_TYPE_UINT32 = 4;
    public const int SLANG_SCALAR_TYPE_INT64 = 5;
    public const int SLANG_SCALAR_TYPE_UINT64 = 6;
    public const int SLANG_SCALAR_TYPE_FLOAT16 = 7;
    public const int SLANG_SCALAR_TYPE_FLOAT32 = 8;
    public const int SLANG_SCALAR_TYPE_FLOAT64 = 9;
    public const int SLANG_SCALAR_TYPE_INT8 = 10;
    public const int SLANG_SCALAR_TYPE_UINT8 = 11;
    public const int SLANG_SCALAR_TYPE_INT16 = 12;
    public const int SLANG_SCALAR_TYPE_UINT16 = 13;

    // ── SlangPathKind / SlangPathType / SlangOSPathKind ──
    public const int SLANG_PATH_TYPE_DIRECTORY = 0;
    public const int SLANG_PATH_TYPE_FILE = 1;

    // ── SlangImageFormat ──
    public const int SLANG_IMAGE_FORMAT_unknown = 0;
    public const int SLANG_IMAGE_FORMAT_rgba32f = 1;
    public const int SLANG_IMAGE_FORMAT_rgba16f = 2;
    public const int SLANG_IMAGE_FORMAT_rg32f = 3;
    public const int SLANG_IMAGE_FORMAT_rg16f = 4;
    public const int SLANG_IMAGE_FORMAT_r11f_g11f_b10f = 5;
    public const int SLANG_IMAGE_FORMAT_r32f = 6;
    public const int SLANG_IMAGE_FORMAT_r16f = 7;
    public const int SLANG_IMAGE_FORMAT_rgba16 = 8;
    public const int SLANG_IMAGE_FORMAT_rgb10_a2 = 9;
    public const int SLANG_IMAGE_FORMAT_rgba8 = 10;
    public const int SLANG_IMAGE_FORMAT_rg16 = 11;
    public const int SLANG_IMAGE_FORMAT_rg8 = 12;
    public const int SLANG_IMAGE_FORMAT_r16 = 13;
    public const int SLANG_IMAGE_FORMAT_r8 = 14;
    public const int SLANG_IMAGE_FORMAT_rgba16_snorm = 15;
    public const int SLANG_IMAGE_FORMAT_rgba8_snorm = 16;
    public const int SLANG_IMAGE_FORMAT_rg16_snorm = 17;
    public const int SLANG_IMAGE_FORMAT_rg8_snorm = 18;
    public const int SLANG_IMAGE_FORMAT_r16_snorm = 19;
    public const int SLANG_IMAGE_FORMAT_r8_snorm = 20;
    public const int SLANG_IMAGE_FORMAT_rgba32i = 21;
    public const int SLANG_IMAGE_FORMAT_rgba16i = 22;
    public const int SLANG_IMAGE_FORMAT_rgba8i = 23;
    public const int SLANG_IMAGE_FORMAT_rg32i = 24;
    public const int SLANG_IMAGE_FORMAT_rg16i = 25;
    public const int SLANG_IMAGE_FORMAT_rg8i = 26;
    public const int SLANG_IMAGE_FORMAT_r32i = 27;
    public const int SLANG_IMAGE_FORMAT_r16i = 28;
    public const int SLANG_IMAGE_FORMAT_r8i = 29;
    public const int SLANG_IMAGE_FORMAT_rgba32ui = 30;
    public const int SLANG_IMAGE_FORMAT_rgba16ui = 31;
    public const int SLANG_IMAGE_FORMAT_rgb10_a2ui = 32;
    public const int SLANG_IMAGE_FORMAT_rgba8ui = 33;
    public const int SLANG_IMAGE_FORMAT_rg32ui = 34;
    public const int SLANG_IMAGE_FORMAT_rg16ui = 35;
    public const int SLANG_IMAGE_FORMAT_rg8ui = 36;
    public const int SLANG_IMAGE_FORMAT_r32ui = 37;
    public const int SLANG_IMAGE_FORMAT_r16ui = 38;
    public const int SLANG_IMAGE_FORMAT_r8ui = 39;
    public const int SLANG_IMAGE_FORMAT_r64ui = 40;
    public const int SLANG_IMAGE_FORMAT_r64i = 41;
    public const int SLANG_IMAGE_FORMAT_bgra8 = 42;

    // ── slang::CompilerOptionName (subset used by the engine) ──
    public const int SLANG_COMPILER_OPTION_MATRIX_LAYOUT_COLUMN = 8;
    public const int SLANG_COMPILER_OPTION_OPTIMIZATION = 46;
    public const int SLANG_COMPILER_OPTION_EMIT_SPIRV_DIRECTLY = 58;

    // ── process entry points ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int slang_createGlobalSession(nint apiVersion, out IntPtr outGlobalSession);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr slang_createBlob(byte[] data, nuint size);

    // Flat C export restoring a module from serialized IR (slang.h):
    // IModule* slang_loadModuleFromIRBlob(ISession*, const char* moduleName,
    //     const char* path, const void* source, size_t sourceSize, ISlangBlob** outDiagnostics)
    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static unsafe extern IntPtr slang_loadModuleFromIRBlob(
        IntPtr session, IntPtr moduleName, IntPtr path, byte* source, nuint sourceSize, IntPtr* outDiagnostics);

    // Reflection pointers are opaque SlangReflection* family handles; the
    // compile-side interfaces hand out the same pointers (slang::ProgramLayout*
    // aliases SlangReflection).

    // ── reflection: program parameters ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflection_getGlobalParamsTypeLayout(IntPtr reflection);

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
    public static extern int spReflectionVariableLayout_getStage(IntPtr variableLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionVariable_GetName(IntPtr variable);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionVariable_GetUserAttributeCount(IntPtr variable);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionVariable_GetUserAttribute(IntPtr variable, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionUserAttribute_GetName(IntPtr attribute);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionType_GetUserAttributeCount(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionEntryPoint_getFunction(IntPtr entryPoint);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionFunction_GetUserAttributeCount(IntPtr function);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionFunction_GetUserAttribute(IntPtr function, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionUserAttribute_GetArgumentCount(IntPtr attribute);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionUserAttribute_GetArgumentValueString(IntPtr attribute, uint index, IntPtr outDiagnostics);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionParameter_GetBindingIndex(IntPtr parameter);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionParameter_GetBindingSpace(IntPtr parameter);

    // ── reflection: type layouts (binding ranges API) ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionTypeLayout_GetType(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionTypeLayout_getKind(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint spReflectionTypeLayout_GetSize(IntPtr typeLayout, int category);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionTypeLayout_GetElementTypeLayout(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern nuint spReflectionType_GetElementCount(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl, EntryPoint = "spReflectionTypeLayout_GetElementStride")]
    public static extern nuint spReflectionTypeLayout_getStride(IntPtr typeLayout, int category);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionTypeLayout_GetFieldCount(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionTypeLayout_GetFieldByIndex(IntPtr typeLayout, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionTypeLayout_GetCategoryCount(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionTypeLayout_GetCategoryByIndex(IntPtr typeLayout, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionTypeLayout_getBindingRangeCount(IntPtr typeLayout);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionTypeLayout_getBindingRangeType(IntPtr typeLayout, int index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionTypeLayout_getBindingRangeBindingCount(IntPtr typeLayout, int index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionTypeLayout_getBindingRangeLeafTypeLayout(IntPtr typeLayout, int index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionTypeLayout_getBindingRangeLeafVariable(IntPtr typeLayout, int index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionTypeLayout_getBindingRangeImageFormat(IntPtr typeLayout, int index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionTypeLayout_getFieldBindingRangeOffset(IntPtr typeLayout, int fieldIndex);

    // ── reflection: types ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionType_GetKind(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionType_GetName(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionType_GetResourceShape(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionType_GetResourceAccess(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionType_GetResourceResultType(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionType_GetColumnCount(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionType_GetRowCount(IntPtr type);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionType_GetScalarType(IntPtr type);

    // ── slang::SlangDeclKind (module declaration tree) ──
    public const int SLANG_DECL_KIND_UNSUPPORTED = 0;
    public const int SLANG_DECL_KIND_STRUCT = 1;
    public const int SLANG_DECL_KIND_FUNC = 2;
    public const int SLANG_DECL_KIND_MODULE = 3;
    public const int SLANG_DECL_KIND_GENERIC = 4;
    public const int SLANG_DECL_KIND_VARIABLE = 5;
    public const int SLANG_DECL_KIND_NAMESPACE = 6;
    public const int SLANG_DECL_KIND_ENUM = 7;

    // ── reflection: module declaration tree (type discovery) ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionDecl_getChildrenCount(IntPtr parentDecl);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionDecl_getChild(IntPtr parentDecl, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionDecl_getName(IntPtr decl);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionDecl_getKind(IntPtr decl);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionDecl_castToGeneric(IntPtr decl);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflection_getTypeFromDecl(IntPtr decl);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflection_FindTypeByName(IntPtr reflection, IntPtr name);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool spReflection_isSubType(IntPtr reflection, IntPtr subType, IntPtr superType);

    // specializeType(genericType, args...) → TypeReflection* of the applied
    // generic — type-level composition without a wrapper module.
    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static unsafe extern IntPtr spReflection_specializeType(
        IntPtr reflection, IntPtr type, nint specializationArgCount,
        IntPtr* specializationArgs, IntPtr* outDiagnostics);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionVariable_GetType(IntPtr variable);

    // ── reflection: generic containers (entry-point generic parameters) ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionGeneric_GetTypeParameterCount(IntPtr generic);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionGeneric_GetTypeParameter(IntPtr generic, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionGeneric_GetTypeParameterConstraintCount(IntPtr generic, IntPtr typeParam);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionGeneric_GetTypeParameterConstraintType(IntPtr generic, IntPtr typeParam, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionGeneric_GetValueParameterCount(IntPtr generic);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionGeneric_GetValueParameter(IntPtr generic, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionGeneric_GetInnerDecl(IntPtr generic);

    // ── reflection: entry points ──

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionEntryPoint_getName(IntPtr entryPoint);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern int spReflectionEntryPoint_getStage(IntPtr entryPoint);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint spReflectionEntryPoint_getParameterCount(IntPtr entryPoint);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionEntryPoint_getParameterByIndex(IntPtr entryPoint, uint index);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr spReflectionEntryPoint_getResultVarLayout(IntPtr entryPoint);

    [DllImport(Slang, CallingConvention = CallingConvention.Cdecl)]
    public static extern void spReflectionEntryPoint_getComputeThreadGroupSize(IntPtr entryPoint, nuint axisCount, [Out] nuint[] outSizeAlongAxis);

    /// <summary>Marshal a slang UTF-8 char* return value; null-safe.</summary>
    public static string? StringFromPtr(IntPtr ptr)
    {
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ptr);
    }
}
