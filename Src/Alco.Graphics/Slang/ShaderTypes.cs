// Public shader target, stage, optimization, and compiler configuration types for NoGraphicsSharp.

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Target bytecode or source format corresponding to an NGS backend.</summary>
public enum ShaderTarget : byte
{
    /// <summary>SPIR-V 1.5 for Vulkan with descriptor heaps and mesh shading.</summary>
    Spirv = 0,

    /// <summary>SPIR-V 1.5 lowered for Vulkan Compact with indexed descriptor sets and physical buffer addresses.</summary>
    SpirvCompact = 1,

    /// <summary>Shader Model 6.6 DXIL for Direct3D 12.</summary>
    Dxil = 2,

    /// <summary>Metal Shading Language source compiled by Metal 4 at pipeline creation.</summary>
    MetalSource = 3,

    /// <summary>Precompiled Metal library container built through an available Apple Metal toolchain.</summary>
    MetalLibrary = 4,
}

/// <summary>Programmable pipeline stages supported by NGS shaders; combinable flags.</summary>
[Flags]
public enum ShaderStageKind : uint
{
    /// <summary>No shader stage.</summary>
    None = 0,

    /// <summary>The vertex stage.</summary>
    Vertex = 1u << 0,

    /// <summary>The hull (tessellation control) stage.</summary>
    Hull = 1u << 1,

    /// <summary>The domain (tessellation evaluation) stage.</summary>
    Domain = 1u << 2,

    /// <summary>The geometry stage.</summary>
    Geometry = 1u << 3,

    /// <summary>The pixel (fragment) stage.</summary>
    Pixel = 1u << 4,

    /// <summary>The compute stage.</summary>
    Compute = 1u << 5,

    /// <summary>The mesh stage.</summary>
    Mesh = 1u << 6,

    /// <summary>The task (amplification) stage.</summary>
    Task = 1u << 7,
}

/// <summary>Pipeline family used to automatically select entry points from a shader module.</summary>
public enum ShaderPipelineKind : byte
{
    /// <summary>One vertex stage and at most one pixel stage.</summary>
    Graphics = 0,

    /// <summary>One compute stage.</summary>
    Compute = 1,

    /// <summary>One mesh stage with optional task and pixel stages.</summary>
    Mesh = 2,

    /// <summary>Every entry point defined in the module.</summary>
    All = 3,

    /// <summary>Automatically infers Graphics, Compute, or Mesh from the module's declared entry points.</summary>
    Auto = 4,
}

/// <summary>Optimization level applied during shader compilation.</summary>
public enum ShaderOptimization : byte
{
    /// <summary>Disables compiler optimizations.</summary>
    None = 0,

    /// <summary>Applies baseline optimizations.</summary>
    Default = 1,

    /// <summary>Applies aggressive optimizations.</summary>
    High = 2,

    /// <summary>Applies maximal optimizations; the default.</summary>
    Maximal = 3,
}

/// <summary>Resolves virtual shader file contents as UTF-8 text, or returns null when absent.</summary>
/// <param name="path">The normalized virtual or search-resolved shader path.</param>
/// <returns>The source text, or null to fall back to search paths or built-in modules.</returns>
public delegate string? ShaderFileResolver(string path);

/// <summary>Checks whether a virtual shader path exists before loading it.</summary>
/// <param name="path">The normalized shader path.</param>
/// <returns>True when the resolver can serve the path.</returns>
public delegate bool ShaderPathExists(string path);

/// <summary>Explicit entry point request combining source function name and expected stage.</summary>
/// <param name="Name">The entry point function name in shader source.</param>
/// <param name="Stage">The expected programmable stage.</param>
public readonly record struct ShaderEntryRequest(string Name, ShaderStageKind Stage);

/// <summary>Session-wide search paths, virtual file callbacks, defines, and ABI validation rules.</summary>
public sealed class ShaderCompilerOptions
{
    /// <summary>Default compiler options searching the application base directory with maximal optimization and ABI validation.</summary>
    public static readonly ShaderCompilerOptions Default = new();

    /// <summary>Directories searched for shader modules and includes; defaults to the application base directory.</summary>
    public IReadOnlyList<string> SearchPaths { get; init; } = [AppContext.BaseDirectory];

    /// <summary>Preprocessor macros applied across compilations unless overridden per request.</summary>
    public IReadOnlyList<(string Name, string Value)> Defines { get; init; } = [];

    /// <summary>Optional virtual file resolver consulted before disk search paths and built-in NGS headers.</summary>
    public ShaderFileResolver? FileResolver { get; init; }

    /// <summary>Optional fast existence probe paired with <see cref="FileResolver"/>.</summary>
    public ShaderPathExists? PathExists { get; init; }

    /// <summary>Compiler optimization level; defaults to <see cref="ShaderOptimization.Maximal"/>.</summary>
    public ShaderOptimization Optimization { get; init; } = ShaderOptimization.Maximal;

    /// <summary>Validates NGS bindless ABI rules (global bindings and resolved threadgroup dimensions); defaults to true.</summary>
    public bool ValidateAbi { get; init; } = true;

    /// <summary>Optional directory for persistent compiled shader artifacts keyed by target, options, and source hashes.</summary>
    public string? CacheDirectory { get; init; }

    /// <summary>Optional diagnostic sink for compiler warnings and cache events.</summary>
    public Action<string>? Log { get; init; }
}

/// <summary>Per-compilation preprocessor defines and Slang specialization arguments.</summary>
public sealed class ShaderCompileOptions
{
    /// <summary>Additional preprocessor macros merged on top of the compiler's base defines.</summary>
    public IReadOnlyList<(string Name, string Value)> Defines { get; init; } = [];

    /// <summary>Specialization expressions (concrete types or constant values) in declaration order.</summary>
    public IReadOnlyList<string> SpecializationArgs { get; init; } = [];

    /// <summary>Optional per-compilation override for NGS ABI validation; null inherits compiler options.</summary>
    public bool? ValidateAbi { get; init; }
}
