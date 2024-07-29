namespace SlangSharp;

public enum SlangCompileTarget : int
{
    SLANG_TARGET_UNKNOWN,
    SLANG_TARGET_NONE,
    SLANG_GLSL,
    SLANG_GLSL_VULKAN_DEPRECATED,              //< deprecated and removed: just use `SLANG_GLSL`.
    SLANG_GLSL_VULKAN_ONE_DESC_DEPRECATED,     //< deprecated and removed.
    SLANG_HLSL,
    SLANG_SPIRV,
    SLANG_SPIRV_ASM,
    SLANG_DXBC,
    SLANG_DXBC_ASM,
    SLANG_DXIL,
    SLANG_DXIL_ASM,
    SLANG_C_SOURCE,                 // The C language
    SLANG_CPP_SOURCE,               // C++ code for shader kernels.
    SLANG_HOST_EXECUTABLE,          // Standalone binary executable (for hosting CPU/OS)
    SLANG_SHADER_SHARED_LIBRARY,    // A shared library/Dll for shader kernels (for hosting CPU/OS)
    SLANG_SHADER_HOST_CALLABLE,     // A CPU target that makes the compiled shader code available to be run immediately
    SLANG_CUDA_SOURCE,              // Cuda source
    SLANG_PTX,                      // PTX
    SLANG_CUDA_OBJECT_CODE,         // Object code that contains CUDA functions.
    SLANG_OBJECT_CODE,              // Object code that can be used for later linking
    SLANG_HOST_CPP_SOURCE,          // C++ code for host library or executable.
    SLANG_HOST_HOST_CALLABLE,       // Host callable host code (ie non kernel/shader) 
    SLANG_CPP_PYTORCH_BINDING,      // C++ PyTorch binding code.
    SLANG_METAL,                    // Metal shading language
    SLANG_METAL_LIB,                // Metal library
    SLANG_METAL_LIB_ASM,            // Metal library assembly
    SLANG_HOST_SHARED_LIBRARY,      // A shared library/Dll for host code (for hosting CPU/OS)
    SLANG_TARGET_COUNT_OF,
}

public enum SlangLineDirectiveMode : uint
{
    SLANG_LINE_DIRECTIVE_MODE_DEFAULT = 0,
    SLANG_LINE_DIRECTIVE_MODE_NONE,
    SLANG_LINE_DIRECTIVE_MODE_STANDARD,
    SLANG_LINE_DIRECTIVE_MODE_GLSL,
    SLANG_LINE_DIRECTIVE_MODE_SOURCE_MAP,
};

[Flags]
public enum SlangTargetFlags : uint
{
    SLANG_TARGET_FLAG_PARAMETER_BLOCKS_USE_REGISTER_SPACES = 1 << 4,
    SLANG_TARGET_FLAG_GENERATE_WHOLE_PROGRAM = 1 << 8,
    SLANG_TARGET_FLAG_DUMP_IR = 1 << 9,
    SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY = 1 << 10,
}

[Flags]
public enum SlangCompileFlags : int
{
    SLANG_COMPILE_FLAG_NO_MANGLING = 1 << 3,

    SLANG_COMPILE_FLAG_NO_CODEGEN = 1 << 4,

    SLANG_COMPILE_FLAG_OBFUSCATE = 1 << 5,

    SLANG_COMPILE_FLAG_NO_CHECKING = 0,
    SLANG_COMPILE_FLAG_SPLIT_MIXED_TYPES = 0,
}

public enum SlangFloatingPointMode : int
{
    SLANG_FLOATING_POINT_MODE_DEFAULT = 0,
    SLANG_FLOATING_POINT_MODE_FAST,
    SLANG_FLOATING_POINT_MODE_PRECISE,
}

public enum SlangDebugInfoLevel : uint
{
    SLANG_DEBUG_INFO_LEVEL_NONE = 0,
    SLANG_DEBUG_INFO_LEVEL_MINIMAL,
    SLANG_DEBUG_INFO_LEVEL_STANDARD,
    SLANG_DEBUG_INFO_LEVEL_MAXIMAL,
}

public enum SlangDebugInfoFormat : uint
{
    SLANG_DEBUG_INFO_FORMAT_DEFAULT,         // Use the default debugging format for the target 
    SLANG_DEBUG_INFO_FORMAT_C7,              // CodeView C7 format (typically means debugging infomation is embedded in the binary)
    SLANG_DEBUG_INFO_FORMAT_PDB,             // Program database

    SLANG_DEBUG_INFO_FORMAT_STABS,          // Stabs
    SLANG_DEBUG_INFO_FORMAT_COFF,           // COFF debug info
    SLANG_DEBUG_INFO_FORMAT_DWARF,          // DWARF debug info (we may want to support specifying the version)

    SLANG_DEBUG_INFO_FORMAT_COUNT_OF,
}

public enum SlangOptimizationLevel : uint
{
    SLANG_OPTIMIZATION_LEVEL_NONE = 0,  /** Don't optimize at all. */
    SLANG_OPTIMIZATION_LEVEL_DEFAULT,   /** Default optimization level: balance code quality and compilation time. */
    SLANG_OPTIMIZATION_LEVEL_HIGH,      /** Optimize aggressively. */
    SLANG_OPTIMIZATION_LEVEL_MAXIMAL,   /** Include optimizations that may take a very long time, or may involve severe space-vs-speed tradeoffs */
}

public enum SlangPassThrough : int
{
    SLANG_PASS_THROUGH_NONE,
    SLANG_PASS_THROUGH_FXC,
    SLANG_PASS_THROUGH_DXC,
    SLANG_PASS_THROUGH_GLSLANG,
    SLANG_PASS_THROUGH_SPIRV_DIS,
    SLANG_PASS_THROUGH_CLANG,                   // Clang C/C++ compiler 
    SLANG_PASS_THROUGH_VISUAL_STUDIO,           // Visual studio C/C++ compiler
    SLANG_PASS_THROUGH_GCC,                     // GCC C/C++ compiler
    SLANG_PASS_THROUGH_GENERIC_C_CPP,           // Generic C or C++ compiler, which is decided by the source type
    SLANG_PASS_THROUGH_NVRTC,                   // NVRTC Cuda compiler
    SLANG_PASS_THROUGH_LLVM,                    // LLVM 'compiler' - includes LLVM and Clang
    SLANG_PASS_THROUGH_SPIRV_OPT,               // SPIRV-opt
    SLANG_PASS_THROUGH_COUNT_OF,
}

public enum SlangContainerFormat : int
{
    SLANG_CONTAINER_FORMAT_NONE,
    SLANG_CONTAINER_FORMAT_SLANG_MODULE,
}

public enum SlangDiagnosticFlags : int
{
    SLANG_DIAGNOSTIC_FLAG_VERBOSE_PATHS = 0x01,
    SLANG_DIAGNOSTIC_FLAG_TREAT_WARNINGS_AS_ERRORS = 0x02
}

public enum SlangSourceLanguage : int
{
    SLANG_SOURCE_LANGUAGE_UNKNOWN,
    SLANG_SOURCE_LANGUAGE_SLANG,
    SLANG_SOURCE_LANGUAGE_HLSL,
    SLANG_SOURCE_LANGUAGE_GLSL,
    SLANG_SOURCE_LANGUAGE_C,
    SLANG_SOURCE_LANGUAGE_CPP,
    SLANG_SOURCE_LANGUAGE_CUDA,
    SLANG_SOURCE_LANGUAGE_SPIRV,
    SLANG_SOURCE_LANGUAGE_COUNT_OF,
};

public enum SlangSeverity : int
{
    SLANG_SEVERITY_DISABLED = 0,
    SLANG_SEVERITY_NOTE,
    SLANG_SEVERITY_WARNING,
    SLANG_SEVERITY_ERROR,
    SLANG_SEVERITY_FATAL,
    SLANG_SEVERITY_INTERNAL,
}

public enum SlangStage : uint
{
    SLANG_STAGE_NONE,
    SLANG_STAGE_VERTEX,
    SLANG_STAGE_HULL,
    SLANG_STAGE_DOMAIN,
    SLANG_STAGE_GEOMETRY,
    SLANG_STAGE_FRAGMENT,
    SLANG_STAGE_COMPUTE,
    SLANG_STAGE_RAY_GENERATION,
    SLANG_STAGE_INTERSECTION,
    SLANG_STAGE_ANY_HIT,
    SLANG_STAGE_CLOSEST_HIT,
    SLANG_STAGE_MISS,
    SLANG_STAGE_CALLABLE,
    SLANG_STAGE_MESH,
    SLANG_STAGE_AMPLIFICATION,
    SLANG_STAGE_PIXEL = SLANG_STAGE_FRAGMENT,
}

public enum SlangMatrixLayoutMode : uint
{
    SLANG_MATRIX_LAYOUT_MODE_UNKNOWN = 0,
    SLANG_MATRIX_LAYOUT_ROW_MAJOR,
    SLANG_MATRIX_LAYOUT_COLUMN_MAJOR,
}