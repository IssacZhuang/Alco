// Official Alco.Graphics Slang shader compiler wrapper, built-in virtual file system, ABI validator, and optional disk cache.

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Alco.Graphics.Slang;

using Alco.Graphics.LowLevel;

namespace Alco.Graphics;

/// <summary>Thread-safe Alco.Graphics shader compiler wrapping Slang with automatic backend targeting and ABI enforcement.</summary>
public sealed class ShaderCompiler : IDisposable
{
    private const string BuiltinRoot = "/__alco_graphics_builtin__";
    private const string BuiltinPrefix = "/__alco_graphics_builtin__/";
    private const string BuiltinModuleRelativePath = "Core.slang";
    private const string BuiltinModuleVirtualPath = "/__alco_graphics_builtin__/Core.slang";
    private const string BuiltinSharedSamplerRelativePath = "SharedSampler.slang";
    private const string BuiltinSharedSamplerVirtualPath = "/__alco_graphics_builtin__/SharedSampler.slang";

    private static readonly byte[] BuiltinModuleBytes = LoadEmbeddedShader("Core.slang");
    private static readonly byte[] BuiltinSharedSamplerBytes = LoadEmbeddedShader("SharedSampler.slang");

    private readonly SlangCompiler _nativeCompiler = new();
    private readonly ShaderCompilerOptions _options;
    private readonly SlangCodeTarget _nativeTarget;
    private readonly string[] _effectiveSearchPaths;
    private readonly List<SessionEntry> _sessions = [];
    private readonly ConcurrentDictionary<string, byte[]> _inMemorySources = new(StringComparer.Ordinal);
    private readonly Lock _sessionLock = new();
    private bool _disposed;

    /// <summary>Creates a compiler targeting the backend of <paramref name="device"/>.</summary>
    /// <param name="device">The device whose active backend selects the compiled target format.</param>
    /// <param name="options">Optional search paths, defines, virtual file callbacks, and validation settings.</param>
    public ShaderCompiler(GPUDevice device, ShaderCompilerOptions? options = null)
        : this(TargetForBackend((device ?? throw new ArgumentNullException(nameof(device))).ActiveBackend), options)
    {
    }

    /// <summary>Creates a compiler for an explicit <paramref name="target"/> without requiring a GPU device.</summary>
    /// <param name="target">The shader bytecode or source target format.</param>
    /// <param name="options">Optional search paths, defines, virtual file callbacks, and validation settings.</param>
    public ShaderCompiler(ShaderTarget target, ShaderCompilerOptions? options = null)
    {
        Target = target;
        _nativeTarget = ToInternalTarget(target);
        _options = options ?? ShaderCompilerOptions.Default;
        _effectiveSearchPaths = BuildSearchPaths(_options.SearchPaths);
    }

    /// <summary>The target format emitted by this compiler.</summary>
    public ShaderTarget Target { get; }

    /// <summary>The underlying compiler version tag suitable for external cache keys.</summary>
    public string CompilerVersionTag => _nativeCompiler.BuildTag;

    /// <summary>Maps an Alco.Graphics <see cref="GraphicsBackend"/> to its official <see cref="ShaderTarget"/>.</summary>
    /// <param name="backend">The active graphics backend.</param>
    /// <returns>The matching shader target format.</returns>
    /// <exception cref="GPUException">The backend is <see cref="GraphicsBackend.Auto"/> or unsupported.</exception>
    public static ShaderTarget TargetForBackend(GraphicsBackend backend) => backend switch
    {
        GraphicsBackend.Vulkan => ShaderTarget.Spirv,
        GraphicsBackend.VulkanCompact => ShaderTarget.SpirvCompact,
        GraphicsBackend.Dx12 => ShaderTarget.Dxil,
        GraphicsBackend.Metal => ShaderTarget.MetalSource,
        GraphicsBackend.NoGPU => ShaderTarget.SpirvCompact,
        _ => throw new GPUException(Error.Unsupported, $"No shader target exists for graphics backend '{backend}'."),
    };

    /// <summary>Reports whether <paramref name="target"/> is supported by the compiler runtime on this machine.</summary>
    /// <param name="target">The shader target to query.</param>
    /// <returns>True when compilation for <paramref name="target"/> can run on this host.</returns>
    public static bool IsTargetSupported(ShaderTarget target) =>
        SlangCompiler.CheckTargetSupport(ToInternalTarget(target));

    /// <summary>Returns the entry-point name emitted in compiled code for <paramref name="target"/>.</summary>
    /// <param name="target">The compilation target.</param>
    /// <param name="sourceEntryName">The entry-point function name in shader source.</param>
    /// <returns><c>"main"</c> for SPIR-V targets and <paramref name="sourceEntryName"/> otherwise.</returns>
    public static string GetCompiledEntryPointName(ShaderTarget target, string sourceEntryName)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceEntryName);
        return ToInternalTarget(target).EntryPointName(sourceEntryName);
    }

    /// <summary>Normalizes C# specialization arguments into canonical Slang literal or expression strings.</summary>
    /// <param name="specializations">Boolean, integer, enum, or Slang expression string arguments.</param>
    /// <returns>The normalized specialization strings in argument order.</returns>
    /// <exception cref="ArgumentException">An argument is null or has an unsupported type such as floating-point.</exception>
    public static string[] NormalizeSpecializations(ReadOnlySpan<object> specializations)
    {
        if (specializations.IsEmpty)
            return [];

        string[] normalized = new string[specializations.Length];
        for (int i = 0; i < specializations.Length; i++)
        {
            normalized[i] = specializations[i] switch
            {
                bool b => b ? "true" : "false",
                int v => v.ToString(CultureInfo.InvariantCulture),
                uint v => v.ToString(CultureInfo.InvariantCulture),
                long v => v.ToString(CultureInfo.InvariantCulture),
                ulong v => v.ToString(CultureInfo.InvariantCulture),
                short v => v.ToString(CultureInfo.InvariantCulture),
                ushort v => v.ToString(CultureInfo.InvariantCulture),
                sbyte v => v.ToString(CultureInfo.InvariantCulture),
                byte v => v.ToString(CultureInfo.InvariantCulture),
                Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
                string s => s,
                null => throw new ArgumentException(
                    "Specialization arguments cannot be null; pass bool, an integer, an enum, or a Slang expression string.", nameof(specializations)),
                _ => throw new ArgumentException(
                    $"Unsupported specialization argument type '{specializations[i].GetType().Name}': " +
                    "pass bool, an integer, an enum, or a Slang expression string.", nameof(specializations)),
            };
        }
        return normalized;
    }

    /// <summary>Loads and parses a shader module by name through search paths and virtual file resolvers.</summary>
    /// <param name="moduleName">The module name without extension or relative module path.</param>
    /// <param name="options">Optional per-request preprocessor defines.</param>
    /// <returns>A parsed module handle valid until this compiler is disposed.</returns>
    public ShaderModule LoadModule(string moduleName, ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        SlangCompileSession session = GetOrCreateSession(options?.Defines);
        SlangModuleHandle handle = session.LoadModule(moduleName);
        return new ShaderModule(this, session, handle, options);
    }

    /// <summary>Loads and parses a shader module from in-memory source text.</summary>
    /// <param name="moduleName">The unique logical module name.</param>
    /// <param name="virtualPath">The virtual source path used for diagnostics and relative includes.</param>
    /// <param name="source">The Slang source code.</param>
    /// <param name="options">Optional per-request preprocessor defines.</param>
    /// <returns>A parsed module handle valid until this compiler is disposed.</returns>
    public ShaderModule LoadModuleFromSource(string moduleName, string virtualPath, string source, ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ArgumentException.ThrowIfNullOrEmpty(virtualPath);
        ArgumentNullException.ThrowIfNull(source);
        _inMemorySources[SlangPathUtility.NormalizePath(virtualPath)] = Encoding.UTF8.GetBytes(source);
        SlangCompileSession session = GetOrCreateSession(options?.Defines);
        SlangModuleHandle handle = session.LoadModuleFromSource(moduleName, virtualPath, source);
        return new ShaderModule(this, session, handle, options);
    }

    /// <summary>Compiles a module's entry points selected automatically by <paramref name="pipeline"/>.</summary>
    /// <param name="moduleName">The shader module name resolved through search paths.</param>
    /// <param name="pipeline">The pipeline family used to select entry points automatically.</param>
    /// <param name="options">Optional defines, specialization arguments, and ABI override.</param>
    /// <returns>The compiled shader with target code and pure-managed reflection.</returns>
    public CompiledShader Compile(string moduleName, ShaderPipelineKind pipeline = ShaderPipelineKind.All, ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        if (TryLoadFromCache(moduleName, sourceHash: null, pipeline.ToString(), options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModule(moduleName, options);
        CompiledShader compiled = module.Compile(pipeline, options);
        TrySaveToCache(moduleName, sourceHash: null, primaryVirtualPath: null, pipeline.ToString(), options, compiled);
        return compiled;
    }

    /// <summary>Compiles specific entry points by source function name, inferring each stage from shader reflection.</summary>
    /// <param name="moduleName">The shader module name resolved through search paths.</param>
    /// <param name="entryPointNames">The source entry-point names to compile in output order.</param>
    /// <param name="options">Optional defines, specialization arguments, and ABI override.</param>
    /// <returns>The compiled shader with target code and pure-managed reflection.</returns>
    public CompiledShader Compile(string moduleName, ReadOnlySpan<string> entryPointNames, ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        string selectorKey = BuildEntryNameKey(entryPointNames);
        if (TryLoadFromCache(moduleName, sourceHash: null, selectorKey, options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModule(moduleName, options);
        CompiledShader compiled = module.Compile(entryPointNames, options);
        TrySaveToCache(moduleName, sourceHash: null, primaryVirtualPath: null, selectorKey, options, compiled);
        return compiled;
    }

    /// <summary>Compiles specific entry points while validating each entry point's declared stage.</summary>
    /// <param name="moduleName">The shader module name resolved through search paths.</param>
    /// <param name="entryPoints">The entry-point names and expected stages in output order.</param>
    /// <param name="options">Optional defines, specialization arguments, and ABI override.</param>
    /// <returns>The compiled shader with target code and pure-managed reflection.</returns>
    public CompiledShader Compile(string moduleName, ReadOnlySpan<ShaderEntryRequest> entryPoints, ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        string selectorKey = BuildEntryRequestKey(entryPoints);
        if (TryLoadFromCache(moduleName, sourceHash: null, selectorKey, options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModule(moduleName, options);
        CompiledShader compiled = module.Compile(entryPoints, options);
        TrySaveToCache(moduleName, sourceHash: null, primaryVirtualPath: null, selectorKey, options, compiled);
        return compiled;
    }

    /// <summary>Compiles in-memory source selecting entry points automatically by <paramref name="pipeline"/>.</summary>
    /// <param name="moduleName">The logical module name.</param>
    /// <param name="virtualPath">The virtual path used for diagnostics and relative includes.</param>
    /// <param name="source">The Slang source text.</param>
    /// <param name="pipeline">The pipeline family used to select entry points automatically.</param>
    /// <param name="options">Optional defines, specialization arguments, and ABI override.</param>
    /// <returns>The compiled shader with target code and pure-managed reflection.</returns>
    public CompiledShader CompileSource(
        string moduleName,
        string virtualPath,
        string source,
        ShaderPipelineKind pipeline = ShaderPipelineKind.All,
        ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ArgumentException.ThrowIfNullOrEmpty(virtualPath);
        ArgumentNullException.ThrowIfNull(source);
        string sourceHash = ComputeTextHash(source);
        if (TryLoadFromCache(moduleName, sourceHash, pipeline.ToString(), options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModuleFromSource(moduleName, virtualPath, source, options);
        CompiledShader compiled = module.Compile(pipeline, options);
        TrySaveToCache(moduleName, sourceHash, virtualPath, pipeline.ToString(), options, compiled);
        return compiled;
    }

    /// <summary>Compiles in-memory source for the named entry points, inferring stages from shader reflection.</summary>
    /// <param name="moduleName">The logical module name.</param>
    /// <param name="virtualPath">The virtual path used for diagnostics and relative includes.</param>
    /// <param name="source">The Slang source text.</param>
    /// <param name="entryPointNames">The source entry-point names to compile in output order.</param>
    /// <param name="options">Optional defines, specialization arguments, and ABI override.</param>
    /// <returns>The compiled shader with target code and pure-managed reflection.</returns>
    public CompiledShader CompileSource(
        string moduleName,
        string virtualPath,
        string source,
        ReadOnlySpan<string> entryPointNames,
        ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ArgumentException.ThrowIfNullOrEmpty(virtualPath);
        ArgumentNullException.ThrowIfNull(source);
        string sourceHash = ComputeTextHash(source);
        string selectorKey = BuildEntryNameKey(entryPointNames);
        if (TryLoadFromCache(moduleName, sourceHash, selectorKey, options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModuleFromSource(moduleName, virtualPath, source, options);
        CompiledShader compiled = module.Compile(entryPointNames, options);
        TrySaveToCache(moduleName, sourceHash, virtualPath, selectorKey, options, compiled);
        return compiled;
    }

    /// <summary>Compiles in-memory source for explicit entry points while validating each entry point's stage.</summary>
    /// <param name="moduleName">The logical module name.</param>
    /// <param name="virtualPath">The virtual path used for diagnostics and relative includes.</param>
    /// <param name="source">The Slang source text.</param>
    /// <param name="entryPoints">The entry-point names and expected stages in output order.</param>
    /// <param name="options">Optional defines, specialization arguments, and ABI override.</param>
    /// <returns>The compiled shader with target code and pure-managed reflection.</returns>
    public CompiledShader CompileSource(
        string moduleName,
        string virtualPath,
        string source,
        ReadOnlySpan<ShaderEntryRequest> entryPoints,
        ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ArgumentException.ThrowIfNullOrEmpty(virtualPath);
        ArgumentNullException.ThrowIfNull(source);
        string sourceHash = ComputeTextHash(source);
        string selectorKey = BuildEntryRequestKey(entryPoints);
        if (TryLoadFromCache(moduleName, sourceHash, selectorKey, options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModuleFromSource(moduleName, virtualPath, source, options);
        CompiledShader compiled = module.Compile(entryPoints, options);
        TrySaveToCache(moduleName, sourceHash, virtualPath, selectorKey, options, compiled);
        return compiled;
    }

    /// <summary>Compiles a pass template module composed with a surface module implementing its contract interface.</summary>
    /// <param name="templateModuleName">The template module declaring generic entry points.</param>
    /// <param name="surfaceModuleName">The companion module exporting a single struct implementing the template's contract.</param>
    /// <param name="pipeline">The pipeline family used to select entry points; defaults to automatic inference.</param>
    /// <param name="options">Optional defines, value specialization arguments, and ABI override.</param>
    /// <returns>The compiled composed shader with target code and pure-managed reflection.</returns>
    public CompiledShader CompileComposed(
        string templateModuleName,
        string surfaceModuleName,
        ShaderPipelineKind pipeline = ShaderPipelineKind.Auto,
        ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(templateModuleName);
        ArgumentException.ThrowIfNullOrEmpty(surfaceModuleName);
        string composedKey = $"{templateModuleName}+{surfaceModuleName}";
        if (TryLoadFromCache(composedKey, sourceHash: null, pipeline.ToString(), options, out CompiledShader? cached))
            return cached!;
        ShaderModule template = LoadModule(templateModuleName, options);
        ShaderModule surface = LoadModule(surfaceModuleName, options);
        CompiledShader compiled = template.CompileComposed(surface, pipeline, options);
        TrySaveToCache(composedKey, sourceHash: null, primaryVirtualPath: null, pipeline.ToString(), options, compiled);
        return compiled;
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal CompiledShader CompileModuleInternal(
        ShaderModule module,
        IReadOnlyList<SlangEntryPointRequest> requests,
        ShaderCompileOptions? options)
    {
        ThrowIfDisposed();
        IReadOnlyList<string> specArgs = options?.SpecializationArgs ?? [];
        using SlangProgram program = module.Session.Compile(module.Handle, requests, specArgs);
        return BuildCompiledShader(program, module.DependencyPaths, options);
    }

    internal CompiledShader CompileComposedInternal(
        ShaderModule templateModule,
        ShaderModule surfaceModule,
        IReadOnlyList<SlangEntryPointRequest> requests,
        ShaderCompileOptions? options)
    {
        ThrowIfDisposed();
        if (templateModule.Session != surfaceModule.Session)
        {
            throw new ShaderCompilationException(
                $"Cannot compose template '{templateModule.Name}' and surface '{surfaceModule.Name}' from different compiler sessions.",
                Target, $"{templateModule.Name}+{surfaceModule.Name}");
        }
        IReadOnlyList<string> specArgs = options?.SpecializationArgs ?? [];
        using SlangProgram program = templateModule.Session.CompileComposed(templateModule.Handle, surfaceModule.Handle, requests, specArgs);
        string[] deps = MergeDependencyPaths(templateModule.DependencyPaths, surfaceModule.DependencyPaths);
        return BuildCompiledShader(program, deps, options);
    }

    private CompiledShader BuildCompiledShader(
        SlangProgram program,
        IReadOnlyList<string> dependencyPaths,
        ShaderCompileOptions? options)
    {
        bool validateAbi = options?.ValidateAbi ?? _options.ValidateAbi;
        if (validateAbi)
            ValidateProgramAbi(program.ModuleName, Target, program.Reflection);

        CompiledShaderStage[] stages = new CompiledShaderStage[program.EntryPoints.Length];
        for (int i = 0; i < stages.Length; i++)
        {
            SlangEntryPointInfo info = program.EntryPoints[i];
            stages[i] = new CompiledShaderStage
            {
                SourceName = info.Name,
                CompiledEntryPoint = _nativeTarget.EntryPointName(info.Name),
                Stage = SlangLayoutReader.ToPublicStage(info.Stage),
                ThreadgroupSize = new Extent3D(info.ThreadgroupSize.X, info.ThreadgroupSize.Y, info.ThreadgroupSize.Z),
                Code = program.EntryCode[i],
            };
        }

        return new CompiledShader
        {
            ModuleName = program.ModuleName,
            Target = Target,
            Diagnostics = program.Diagnostics,
            Stages = stages,
            DependencyPaths = dependencyPaths,
            Reflection = program.Reflection,
        };
    }

    private static string[] MergeDependencyPaths(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        List<string> merged = new(left.Count + right.Count);
        for (int i = 0; i < left.Count; i++)
            merged.Add(left[i]);
        for (int i = 0; i < right.Count; i++)
        {
            if (!merged.Contains(right[i], StringComparer.Ordinal))
                merged.Add(right[i]);
        }
        return merged.ToArray();
    }

    internal static string[] FilterDependencyPaths(List<string> rawPaths)
    {
        if (rawPaths.Count == 0)
            return [];
        List<string> filtered = new(rawPaths.Count);
        for (int i = 0; i < rawPaths.Count; i++)
        {
            string normalized = SlangPathUtility.NormalizePath(rawPaths[i]);
            if (normalized.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
                continue;
            if (!filtered.Contains(normalized, StringComparer.Ordinal))
                filtered.Add(normalized);
        }
        return filtered.ToArray();
    }

    private SlangCompileSession GetOrCreateSession(IReadOnlyList<(string Name, string Value)>? requestDefines)
    {
        List<(string Name, string Value)> merged = MergeDefines(_options.Defines, requestDefines);
        if (Target is ShaderTarget.MetalSource or ShaderTarget.MetalLibrary)
            AddDefineIfAbsent(merged, "ALCO_GRAPHICS_METAL", "1");
        string key = BuildDefinesKey(merged);

        lock (_sessionLock)
        {
            ThrowIfDisposed();
            for (int i = 0; i < _sessions.Count; i++)
            {
                if (_sessions[i].Key == key)
                    return _sessions[i].Session;
            }
            SlangCompileSession created = _nativeCompiler.CreateSession(new SlangCompilerOptions
            {
                SearchPaths = _effectiveSearchPaths,
                PreprocessorMacros = merged,
                ByteLoader = LoadVirtualOrDiskBytes,
                PathProbe = ProbeVirtualOrDiskPath,
                PathCanonicalizer = CanonicalizeVirtualOrDiskPath,
                OptimizationLevel = (int)_options.Optimization,
                Target = _nativeTarget,
                MatrixLayoutMode = SlangMatrixLayoutMode.RowMajor,
                ForceCLayout = true,
                Log = _options.Log,
            });
            _sessions.Add(new SessionEntry(key, created));
            return created;
        }
    }

    private byte[]? LoadVirtualOrDiskBytes(string path)
    {
        string norm = SlangPathUtility.NormalizePath(path);
        if (norm == BuiltinModuleVirtualPath)
            return BuiltinModuleBytes;
        if (norm == BuiltinSharedSamplerVirtualPath)
            return BuiltinSharedSamplerBytes;
        if (norm.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
            return null;

        if (_options.FileResolver != null)
        {
            string? resolved = _options.FileResolver(norm);
            if (resolved != null)
                return Encoding.UTF8.GetBytes(resolved);
        }

        if (File.Exists(norm))
            return File.ReadAllBytes(norm);

        if (_inMemorySources.TryGetValue(norm, out byte[]? inMemory))
            return inMemory;

        if (norm.Equals(BuiltinModuleRelativePath, StringComparison.OrdinalIgnoreCase))
            return BuiltinModuleBytes;
        if (norm.Equals(BuiltinSharedSamplerRelativePath, StringComparison.OrdinalIgnoreCase))
            return BuiltinSharedSamplerBytes;

        return null;
    }

    private SlangPathKind ProbeVirtualOrDiskPath(string path)
    {
        string norm = SlangPathUtility.NormalizePath(path);
        if (norm == BuiltinRoot)
            return SlangPathKind.Directory;
        if (norm == BuiltinModuleVirtualPath || norm == BuiltinSharedSamplerVirtualPath)
            return SlangPathKind.File;
        if (norm.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
            return SlangPathKind.None;

        if (_options.PathExists != null && _options.PathExists(norm))
            return SlangPathKind.File;
        if (_options.FileResolver != null && _options.FileResolver(norm) != null)
            return SlangPathKind.File;
        if (File.Exists(norm))
            return SlangPathKind.File;
        if (Directory.Exists(norm))
            return SlangPathKind.Directory;
        if (_inMemorySources.ContainsKey(norm))
            return SlangPathKind.File;
        if (norm.Equals(BuiltinModuleRelativePath, StringComparison.OrdinalIgnoreCase) ||
            norm.Equals(BuiltinSharedSamplerRelativePath, StringComparison.OrdinalIgnoreCase))
            return SlangPathKind.File;

        return SlangPathKind.None;
    }

    private string CanonicalizeVirtualOrDiskPath(string path)
    {
        string norm = SlangPathUtility.NormalizePath(path);
        if (norm == BuiltinModuleVirtualPath || norm == BuiltinSharedSamplerVirtualPath)
            return norm;
        if (_options.FileResolver != null && _options.FileResolver(norm) != null)
            return norm;
        if (File.Exists(norm))
            return SlangPathUtility.NormalizePath(Path.GetFullPath(norm));
        if (_inMemorySources.ContainsKey(norm))
            return norm;
        if (norm.Equals(BuiltinModuleRelativePath, StringComparison.OrdinalIgnoreCase))
            return BuiltinModuleVirtualPath;
        if (norm.Equals(BuiltinSharedSamplerRelativePath, StringComparison.OrdinalIgnoreCase))
            return BuiltinSharedSamplerVirtualPath;
        return norm;
    }

    private static void ValidateProgramAbi(string moduleName, ShaderTarget target, ShaderReflection reflection)
    {
        bool hasRoot = reflection.Root != null;
        bool isMetal = target is ShaderTarget.MetalSource or ShaderTarget.MetalLibrary;
        for (int i = 0; i < reflection.Globals.Count; i++)
        {
            ShaderParameterInfo global = reflection.Globals[i];
            bool isRootStorage = hasRoot && global.BindingSpace == 0 &&
                (isMetal || global.BindingIndex == 0) &&
                global.Name == "gpu_root_storage";
            bool isBufferTable = global.BindingSpace == 0 &&
                (isMetal || global.BindingIndex == 3) &&
                global.Name == "gpu_buffer_table";
            bool isMetalPool = isMetal &&
                global.Name is "gpu_texture_pool_base" or "gpu_sampler_heap";
            if (!isRootStorage && !isBufferTable && !isMetalPool)
            {
                throw new ShaderCompilationException(
                    $"Shader '{moduleName}' declares global parameter '{global.Name}' (space {global.BindingSpace}, binding {global.BindingIndex}), " +
                    "violating the Alco.Graphics bindless ABI. Move parameters, GPUBufferView, GpuTextureId, or GpuSamplerId fields into the [GPURoot] struct.",
                    target, moduleName);
            }
        }
    }

    private bool TryLoadFromCache(string moduleName, string? sourceHash, string selectorKey, ShaderCompileOptions? options, out CompiledShader? shader)
    {
        shader = null;
        string? cacheDir = _options.CacheDirectory;
        if (string.IsNullOrEmpty(cacheDir))
            return false;

        string key = BuildCacheKey(moduleName, sourceHash, selectorKey, options);
        string artifactPath = Path.Combine(cacheDir, key + ".agshader");
        string depsPath = Path.Combine(cacheDir, key + ".deps");
        if (!File.Exists(artifactPath) || !File.Exists(depsPath))
            return false;

        try
        {
            string[] lines = File.ReadAllLines(depsPath);
            for (int i = 0; i < lines.Length; i++)
            {
                int sep = lines[i].IndexOf('\t');
                if (sep <= 0)
                    return false;
                string depPath = lines[i][..sep];
                string expectedHash = lines[i][(sep + 1)..];
                byte[]? currentBytes = LoadVirtualOrDiskBytes(depPath);
                if (currentBytes == null || !string.Equals(ComputeBytesHash(currentBytes), expectedHash, StringComparison.Ordinal))
                    return false;
            }
            shader = CompiledShader.Deserialize(File.ReadAllBytes(artifactPath));
            _options.Log?.Invoke($"Shader cache hit: '{moduleName}' ({Target}, {key[..12]}).");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            _options.Log?.Invoke($"Shader cache miss/invalid for '{moduleName}': {exception.Message}");
            shader = null;
            return false;
        }
    }

    private void TrySaveToCache(
        string moduleName, string? sourceHash, string? primaryVirtualPath, string selectorKey, ShaderCompileOptions? options, CompiledShader shader)
    {
        string? cacheDir = _options.CacheDirectory;
        if (string.IsNullOrEmpty(cacheDir))
            return;

        try
        {
            Directory.CreateDirectory(cacheDir);
            string key = BuildCacheKey(moduleName, sourceHash, selectorKey, options);
            string artifactPath = Path.Combine(cacheDir, key + ".agshader");
            string depsPath = Path.Combine(cacheDir, key + ".deps");

            string? normalizedPrimary = sourceHash != null && primaryVirtualPath != null
                ? SlangPathUtility.NormalizePath(primaryVirtualPath)
                : null;
            List<string> depLines = new(shader.DependencyPaths.Count);
            for (int i = 0; i < shader.DependencyPaths.Count; i++)
            {
                string dep = shader.DependencyPaths[i];
                if (normalizedPrimary != null && string.Equals(dep, normalizedPrimary, StringComparison.Ordinal))
                    continue; // Primary in-memory source is already content-hashed in the cache key.
                byte[]? bytes = LoadVirtualOrDiskBytes(dep);
                if (bytes == null)
                    return; // Skip disk caching if a dependency isn't re-readable for validation.
                depLines.Add($"{dep}\t{ComputeBytesHash(bytes)}");
            }

            string tmpArtifact = artifactPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            string tmpDeps = depsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tmpArtifact, shader.Serialize());
                File.WriteAllLines(tmpDeps, depLines, Encoding.UTF8);
                File.Move(tmpArtifact, artifactPath, overwrite: true);
                File.Move(tmpDeps, depsPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmpArtifact)) File.Delete(tmpArtifact);
                if (File.Exists(tmpDeps)) File.Delete(tmpDeps);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _options.Log?.Invoke($"Shader cache write skipped for '{moduleName}': {exception.Message}");
        }
    }

    private string BuildCacheKey(string moduleName, string? sourceHash, string selectorKey, ShaderCompileOptions? options)
    {
        StringBuilder sb = new(256);
        sb.Append("alco-graphics-root-layout-2|").Append(CompilerVersionTag).Append('|')
            .Append((int)Target).Append('|')
            .Append((int)_options.Optimization).Append('|')
            .Append(options?.ValidateAbi ?? _options.ValidateAbi).Append('|')
            .Append(moduleName).Append('|')
            .Append(sourceHash ?? "<file>").Append('|')
            .Append(selectorKey).Append('|');

        List<(string Name, string Value)> defines = MergeDefines(_options.Defines, options?.Defines);
        for (int i = 0; i < defines.Count; i++)
            sb.Append(defines[i].Name).Append('=').Append(defines[i].Value).Append(';');
        sb.Append('|');

        if (options != null)
        {
            for (int i = 0; i < options.SpecializationArgs.Count; i++)
                sb.Append(options.SpecializationArgs[i]).Append(',');
        }

        return ComputeTextHash(sb.ToString());
    }

    private static string BuildEntryNameKey(ReadOnlySpan<string> names)
    {
        StringBuilder sb = new();
        sb.Append("names:");
        for (int i = 0; i < names.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(names[i]);
        }
        return sb.ToString();
    }

    private static string BuildEntryRequestKey(ReadOnlySpan<ShaderEntryRequest> entries)
    {
        StringBuilder sb = new();
        sb.Append("req:");
        for (int i = 0; i < entries.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(entries[i].Name).Append(':').Append((uint)entries[i].Stage);
        }
        return sb.ToString();
    }

    private static string ComputeTextHash(string text) => ComputeBytesHash(Encoding.UTF8.GetBytes(text));

    private static string ComputeBytesHash(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash);
    }

    private static List<(string Name, string Value)> MergeDefines(
        IReadOnlyList<(string Name, string Value)> baseDefines,
        IReadOnlyList<(string Name, string Value)>? extraDefines)
    {
        if (extraDefines == null || extraDefines.Count == 0)
            return [.. baseDefines];
        List<(string Name, string Value)> merged = [.. baseDefines];
        for (int i = 0; i < extraDefines.Count; i++)
        {
            (string name, string value) = extraDefines[i];
            int existing = -1;
            for (int j = 0; j < merged.Count; j++)
            {
                if (merged[j].Name == name)
                {
                    existing = j;
                    break;
                }
            }
            if (existing >= 0)
                merged[existing] = (name, value);
            else
                merged.Add((name, value));
        }
        return merged;
    }

    private static void AddDefineIfAbsent(List<(string Name, string Value)> defines, string name, string value)
    {
        for (int i = 0; i < defines.Count; i++)
        {
            if (defines[i].Name == name)
                return;
        }
        defines.Add((name, value));
    }

    private static string BuildDefinesKey(List<(string Name, string Value)> defines)
    {
        if (defines.Count == 0)
            return string.Empty;
        StringBuilder sb = new();
        for (int i = 0; i < defines.Count; i++)
            sb.Append(defines[i].Name).Append('\0').Append(defines[i].Value).Append('\0');
        return sb.ToString();
    }

    private static string[] BuildSearchPaths(IReadOnlyList<string> userPaths)
    {
        string[] paths = new string[userPaths.Count + 1];
        for (int i = 0; i < userPaths.Count; i++)
            paths[i] = SlangPathUtility.NormalizePath(userPaths[i]);
        paths[^1] = BuiltinRoot;
        return paths;
    }

    private static SlangCodeTarget ToInternalTarget(ShaderTarget target) => target switch
    {
        ShaderTarget.Spirv => SlangCodeTarget.Spirv,
        ShaderTarget.SpirvCompact => SlangCodeTarget.SpirvCompact,
        ShaderTarget.Dxil => SlangCodeTarget.Dxil,
        ShaderTarget.MetalSource => SlangCodeTarget.Msl,
        ShaderTarget.MetalLibrary => SlangCodeTarget.MetalLib,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static byte[] LoadEmbeddedShader(string resourceName)
    {
        Assembly assembly = typeof(ShaderCompiler).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded Alco.Graphics shader resource '{resourceName}'.");
        byte[] bytes = new byte[stream.Length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>Releases all native compiler sessions owned by this compiler. Existing <see cref="CompiledShader"/> instances stay usable.</summary>
    public void Dispose()
    {
        lock (_sessionLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            for (int i = 0; i < _sessions.Count; i++)
                _sessions[i].Session.Dispose();
            _sessions.Clear();
        }
        _nativeCompiler.Dispose();
    }

    private readonly record struct SessionEntry(string Key, SlangCompileSession Session);
}

/// <summary>A parsed shader module handle owned by a live <see cref="ShaderCompiler"/>.</summary>
public sealed class ShaderModule
{
    private readonly ShaderCompiler _compiler;
    private readonly ShaderCompileOptions? _loadOptions;

    internal SlangCompileSession Session { get; }
    internal SlangModuleHandle Handle { get; }

    internal ShaderModule(ShaderCompiler compiler, SlangCompileSession session, SlangModuleHandle handle, ShaderCompileOptions? loadOptions)
    {
        _compiler = compiler;
        Session = session;
        Handle = handle;
        _loadOptions = loadOptions;
        Name = handle.Name;
        FilePath = handle.FilePath;
        EntryPoints = handle.GetDefinedEntryPoints();
        DependencyPaths = ShaderCompiler.FilterDependencyPaths(handle.GetDependencyFilePaths());
    }

    /// <summary>The module name within the compiler session.</summary>
    public string Name { get; }

    /// <summary>The resolved source path, or null when not reported.</summary>
    public string? FilePath { get; }

    /// <summary>All entry points declared in this module before target code generation.</summary>
    public IReadOnlyList<ShaderModuleEntryPoint> EntryPoints { get; }

    /// <summary>Source and transitive include paths referenced by this module, excluding built-in virtual headers.</summary>
    public IReadOnlyList<string> DependencyPaths { get; }

    /// <summary>True when at least one entry point in this module declares generic specialization parameters.</summary>
    public bool HasGenericEntryPoints
    {
        get
        {
            for (int i = 0; i < EntryPoints.Count; i++)
            {
                if (EntryPoints[i].HasGenericParameters)
                    return true;
            }
            return false;
        }
    }

    /// <summary>Infers the unique pipeline family (Graphics, Compute, or Mesh) from this module's declared entry points.</summary>
    /// <returns>The inferred <see cref="ShaderPipelineKind"/>.</returns>
    /// <exception cref="ShaderCompilationException">The module has no entry points or an ambiguous/unsupported stage mix.</exception>
    public ShaderPipelineKind InferPipelineKind()
    {
        if (EntryPoints.Count == 0)
        {
            throw new ShaderCompilationException(
                $"Shader module '{Name}' defines no [shader(...)] entry points.",
                _compiler.Target, Name);
        }

        ShaderStageKind stages = ShaderStageKind.None;
        for (int i = 0; i < EntryPoints.Count; i++)
            stages |= EntryPoints[i].Stage;

        bool hasCompute = (stages & ShaderStageKind.Compute) != 0;
        bool hasVertex = (stages & ShaderStageKind.Vertex) != 0;
        bool hasMesh = (stages & ShaderStageKind.Mesh) != 0;
        bool hasTask = (stages & ShaderStageKind.Task) != 0;

        int families = (hasCompute ? 1 : 0) + (hasVertex ? 1 : 0) + (hasMesh || hasTask ? 1 : 0);
        if (families == 1)
        {
            if (hasCompute && stages == ShaderStageKind.Compute)
                return ShaderPipelineKind.Compute;
            if (hasMesh && (stages & ~(ShaderStageKind.Mesh | ShaderStageKind.Task | ShaderStageKind.Pixel)) == 0)
                return ShaderPipelineKind.Mesh;
            if (hasVertex && (stages & ~(ShaderStageKind.Vertex | ShaderStageKind.Pixel)) == 0)
                return ShaderPipelineKind.Graphics;
        }

        throw new ShaderCompilationException(
            $"Cannot infer a unique pipeline kind for module '{Name}' from stages [{stages}]; specify ShaderPipelineKind explicitly.",
            _compiler.Target, Name);
    }

    /// <summary>Finds all concrete struct types in this module implementing <paramref name="interfaceTypeName"/>.</summary>
    /// <param name="interfaceTypeName">The Slang interface type name.</param>
    /// <returns>The implementing struct type names.</returns>
    public IReadOnlyList<string> FindImplementations(string interfaceTypeName)
    {
        ArgumentException.ThrowIfNullOrEmpty(interfaceTypeName);
        _compiler.ThrowIfDisposed();
        return Handle.FindImplementations(interfaceTypeName);
    }

    /// <summary>Compiles entry points selected automatically by <paramref name="pipeline"/>.</summary>
    /// <param name="pipeline">The pipeline family to compile.</param>
    /// <param name="options">Optional specialization arguments and ABI validation override.</param>
    /// <returns>The compiled shader with target code and reflection.</returns>
    public CompiledShader Compile(ShaderPipelineKind pipeline = ShaderPipelineKind.All, ShaderCompileOptions? options = null)
    {
        _compiler.ThrowIfDisposed();
        ShaderCompileOptions? effective = MergeCompileOptions(options);
        List<SlangEntryPointRequest> selected = SelectEntryPoints(pipeline);
        return _compiler.CompileModuleInternal(this, selected, effective);
    }

    /// <summary>Compiles this template module composed with <paramref name="surfaceModule"/>.</summary>
    /// <param name="surfaceModule">The companion module exporting a single struct implementing this template's contract.</param>
    /// <param name="pipeline">The pipeline family to compile; defaults to automatic inference.</param>
    /// <param name="options">Optional value specialization arguments and ABI validation override.</param>
    /// <returns>The compiled composed shader with target code and reflection.</returns>
    public CompiledShader CompileComposed(
        ShaderModule surfaceModule,
        ShaderPipelineKind pipeline = ShaderPipelineKind.Auto,
        ShaderCompileOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(surfaceModule);
        _compiler.ThrowIfDisposed();
        ShaderCompileOptions? effective = MergeCompileOptions(options);
        List<SlangEntryPointRequest> selected = SelectEntryPoints(pipeline);
        return _compiler.CompileComposedInternal(this, surfaceModule, selected, effective);
    }

    private List<SlangEntryPointRequest> SelectEntryPoints(ShaderPipelineKind pipeline)
    {
        if (pipeline == ShaderPipelineKind.All)
        {
            if (EntryPoints.Count == 0)
            {
                throw new ShaderCompilationException(
                    $"Shader module '{Name}' defines no [shader(...)] entry points.",
                    _compiler.Target, Name);
            }
            List<SlangEntryPointRequest> all = new(EntryPoints.Count);
            for (int i = 0; i < EntryPoints.Count; i++)
                all.Add(new SlangEntryPointRequest(EntryPoints[i].Name, SlangLayoutReader.ToInternalStage(EntryPoints[i].Stage)));
            return all;
        }
        ShaderPipelineKind resolved = pipeline == ShaderPipelineKind.Auto ? InferPipelineKind() : pipeline;
        return SelectForPipeline(resolved);
    }

    /// <summary>Compiles the named entry points, inferring stages from module metadata.</summary>
    /// <param name="entryPointNames">The source entry-point names in output order.</param>
    /// <param name="options">Optional specialization arguments and ABI validation override.</param>
    /// <returns>The compiled shader with target code and reflection.</returns>
    public CompiledShader Compile(ReadOnlySpan<string> entryPointNames, ShaderCompileOptions? options = null)
    {
        _compiler.ThrowIfDisposed();
        if (entryPointNames.IsEmpty)
            throw new ArgumentException("At least one entry point name is required.", nameof(entryPointNames));

        SlangEntryPointRequest[] requests = new SlangEntryPointRequest[entryPointNames.Length];
        for (int i = 0; i < entryPointNames.Length; i++)
        {
            string entryName = entryPointNames[i];
            ShaderStageKind? stage = FindDefinedStage(entryName);
            if (!stage.HasValue)
            {
                throw new ShaderCompilationException(
                    $"Shader entry point '{entryName}' was not found in module '{Name}'.",
                    _compiler.Target, Name);
            }
            requests[i] = new SlangEntryPointRequest(entryName, SlangLayoutReader.ToInternalStage(stage.Value));
        }
        return _compiler.CompileModuleInternal(this, requests, MergeCompileOptions(options));
    }

    /// <summary>Compiles explicit entry points while validating each entry point's stage.</summary>
    /// <param name="entryPoints">The entry-point names and expected stages in output order.</param>
    /// <param name="options">Optional specialization arguments and ABI validation override.</param>
    /// <returns>The compiled shader with target code and reflection.</returns>
    public CompiledShader Compile(ReadOnlySpan<ShaderEntryRequest> entryPoints, ShaderCompileOptions? options = null)
    {
        _compiler.ThrowIfDisposed();
        if (entryPoints.IsEmpty)
            throw new ArgumentException("At least one entry point request is required.", nameof(entryPoints));

        SlangEntryPointRequest[] requests = new SlangEntryPointRequest[entryPoints.Length];
        for (int i = 0; i < entryPoints.Length; i++)
            requests[i] = new SlangEntryPointRequest(entryPoints[i].Name, SlangLayoutReader.ToInternalStage(entryPoints[i].Stage));
        return _compiler.CompileModuleInternal(this, requests, MergeCompileOptions(options));
    }

    private ShaderCompileOptions? MergeCompileOptions(ShaderCompileOptions? callOptions)
    {
        if (callOptions == null)
            return _loadOptions;
        if (_loadOptions == null)
            return callOptions;
        return new ShaderCompileOptions
        {
            Defines = callOptions.Defines.Count > 0 ? callOptions.Defines : _loadOptions.Defines,
            SpecializationArgs = callOptions.SpecializationArgs.Count > 0 ? callOptions.SpecializationArgs : _loadOptions.SpecializationArgs,
            ValidateAbi = callOptions.ValidateAbi ?? _loadOptions.ValidateAbi,
        };
    }

    private ShaderStageKind? FindDefinedStage(string entryName)
    {
        for (int i = 0; i < EntryPoints.Count; i++)
        {
            if (EntryPoints[i].Name == entryName)
                return EntryPoints[i].Stage;
        }
        return null;
    }

    private List<SlangEntryPointRequest> SelectForPipeline(ShaderPipelineKind pipeline)
    {
        ShaderStageKind required = pipeline switch
        {
            ShaderPipelineKind.Graphics => ShaderStageKind.Vertex,
            ShaderPipelineKind.Compute => ShaderStageKind.Compute,
            ShaderPipelineKind.Mesh => ShaderStageKind.Mesh,
            _ => throw new ArgumentOutOfRangeException(nameof(pipeline)),
        };
        ShaderStageKind allowed = required;
        if (pipeline != ShaderPipelineKind.Compute)
            allowed |= ShaderStageKind.Pixel;
        if (pipeline == ShaderPipelineKind.Mesh)
            allowed |= ShaderStageKind.Task;

        List<SlangEntryPointRequest> selected = new(3);
        ShaderStageKind seen = ShaderStageKind.None;
        string?[] firstByBit = new string?[8];

        for (int i = 0; i < EntryPoints.Count; i++)
        {
            ShaderModuleEntryPoint entry = EntryPoints[i];
            if ((allowed & entry.Stage) == 0)
                continue;
            int bit = BitIndex(entry.Stage);
            if ((seen & entry.Stage) != 0)
            {
                throw new ShaderCompilationException(
                    $"Module '{Name}' defines multiple {entry.Stage} entry points ('{firstByBit[bit]}' and '{entry.Name}'); " +
                    "split into one module per pass or select one explicitly.",
                    _compiler.Target, Name);
            }
            seen |= entry.Stage;
            firstByBit[bit] = entry.Name;
            selected.Add(new SlangEntryPointRequest(entry.Name, SlangLayoutReader.ToInternalStage(entry.Stage)));
        }

        if ((seen & required) == 0)
        {
            throw new ShaderCompilationException(
                $"Module '{Name}' defines no {required} entry point for a {pipeline} pipeline.",
                _compiler.Target, Name);
        }
        return selected;
    }

    private static int BitIndex(ShaderStageKind stage) => stage switch
    {
        ShaderStageKind.Vertex => 0,
        ShaderStageKind.Hull => 1,
        ShaderStageKind.Domain => 2,
        ShaderStageKind.Geometry => 3,
        ShaderStageKind.Pixel => 4,
        ShaderStageKind.Compute => 5,
        ShaderStageKind.Mesh => 6,
        ShaderStageKind.Task => 7,
        _ => 0,
    };
}
