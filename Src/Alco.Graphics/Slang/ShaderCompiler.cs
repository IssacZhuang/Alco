// Official Alco.Graphics Slang shader compiler wrapper, built-in virtual file system, ABI validator, and L1/L2 disk caches.

using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Alco.Graphics.LowLevel;
using Alco.Graphics.Slang;

namespace Alco.Graphics;

/// <summary>Thread-safe Alco.Graphics shader compiler wrapping Slang with automatic backend targeting, ABI enforcement, and L1/L2 caching.</summary>
public sealed class ShaderCompiler : IDisposable
{
    private const string BuiltinRoot = "/__alco_graphics_builtin__";
    private const string BuiltinPrefix = "/__alco_graphics_builtin__/";
    private const string BuiltinModuleRelativePath = "Core.slang";
    private const string BuiltinModuleVirtualPath = "/__alco_graphics_builtin__/Core.slang";
    private const string BuiltinSharedSamplerRelativePath = "SharedSampler.slang";
    private const string BuiltinSharedSamplerVirtualPath = "/__alco_graphics_builtin__/SharedSampler.slang";
    private const string VirtualRoot = "/__alco_graphics_virtual__";
    private const string VirtualPrefix = "/__alco_graphics_virtual__/";

    private static readonly byte[] BuiltinModuleBytes = LoadEmbeddedShader("Core.slang");
    private static readonly byte[] BuiltinModuleSha256 = SHA256.HashData(BuiltinModuleBytes);
    private static readonly byte[] BuiltinSharedSamplerBytes = LoadEmbeddedShader("SharedSampler.slang");
    private static readonly byte[] BuiltinSharedSamplerSha256 = SHA256.HashData(BuiltinSharedSamplerBytes);

    private readonly SlangCompiler _nativeCompiler = new();
    private readonly ShaderCompilerOptions _options;
    private readonly SlangCodeTarget _nativeTarget;
    private readonly string[] _effectiveSearchPaths;
    private readonly string _builtinFingerprint;
    private readonly ShaderDiskCache? _diskCache;
    private readonly List<SessionSlot> _sessions = [];
    private readonly ConcurrentDictionary<string, VirtualModuleEntry> _virtualModules = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte[]> _inMemorySources = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _virtualFileAliases = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedSourceHash> _sourceHashCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CachedModuleMetadata> _moduleMetadataCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CompiledShader> _compiledMemoryCache = new(StringComparer.Ordinal);
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
        _builtinFingerprint = BuildBuiltinFingerprint(target, _nativeTarget);
        if (!string.IsNullOrEmpty(_options.CacheDirectory))
            _diskCache = new ShaderDiskCache(_options.CacheDirectory, target, _nativeCompiler.BuildTag, _options.Log);
    }

    /// <summary>The target format emitted by this compiler.</summary>
    public ShaderTarget Target { get; }

    /// <summary>The underlying compiler version tag suitable for external cache keys.</summary>
    public string CompilerVersionTag => _nativeCompiler.BuildTag;

    internal int ActiveSessionCount
    {
        get
        {
            lock (_sessionLock)
                return _sessions.Count;
        }
    }

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

    /// <summary>Registers an in-memory shader module source lazily without initializing a Slang compiler session.</summary>
    /// <param name="moduleName">The unique logical module name.</param>
    /// <param name="virtualPath">The virtual path used for diagnostics, relative includes, and dependency hashing.</param>
    /// <param name="source">The Slang source text.</param>
    public void RegisterVirtualModule(string moduleName, string virtualPath, string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ArgumentException.ThrowIfNullOrEmpty(virtualPath);
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfDisposed();

        string normPath = SlangPathUtility.NormalizePath(virtualPath);
        if (_virtualModules.TryGetValue(moduleName, out VirtualModuleEntry? existing) &&
            string.Equals(existing.VirtualPath, normPath, StringComparison.Ordinal) &&
            string.Equals(existing.Source, source, StringComparison.Ordinal))
        {
            return;
        }

        byte[] utf8 = Encoding.UTF8.GetBytes(source);
        byte[] sha = SHA256.HashData(utf8);
        VirtualModuleEntry entry = new(moduleName, normPath, source, utf8, sha);

        _virtualModules[moduleName] = entry;
        _inMemorySources[normPath] = utf8;
        RegisterVirtualAliases(moduleName, normPath);
        _sourceHashCache[normPath] = CachedSourceHash.ForMemory(sha);

        if (existing != null)
        {
            if (!string.Equals(existing.VirtualPath, normPath, StringComparison.Ordinal))
                _sourceHashCache.TryRemove(existing.VirtualPath, out _);
            InvalidateModuleCaches(moduleName);
        }
    }

    /// <summary>Loads and parses a shader module by name through search paths, virtual modules, and L1 IR cache.</summary>
    /// <param name="moduleName">The module name without extension or relative module path.</param>
    /// <param name="options">Optional per-request preprocessor defines.</param>
    /// <returns>A parsed or IR-restored module handle valid until this compiler is disposed.</returns>
    public ShaderModule LoadModule(string moduleName, ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ThrowIfDisposed();
        if (_virtualModules.TryGetValue(moduleName, out VirtualModuleEntry? vm))
            return LoadModuleCore(moduleName, vm.VirtualPath, vm.Source, vm.Sha256, options);
        return LoadModuleCore(moduleName, explicitVirtualPath: null, explicitSource: null, explicitSha: null, options);
    }

    /// <summary>Loads and parses a shader module from in-memory source text, using L1 IR cache when available.</summary>
    /// <param name="moduleName">The unique logical module name.</param>
    /// <param name="virtualPath">The virtual source path used for diagnostics and relative includes.</param>
    /// <param name="source">The Slang source code.</param>
    /// <param name="options">Optional per-request preprocessor defines.</param>
    /// <returns>A parsed or IR-restored module handle valid until this compiler is disposed.</returns>
    public ShaderModule LoadModuleFromSource(string moduleName, string virtualPath, string source, ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ArgumentException.ThrowIfNullOrEmpty(virtualPath);
        ArgumentNullException.ThrowIfNull(source);
        ThrowIfDisposed();

        RegisterVirtualModule(moduleName, virtualPath, source);
        VirtualModuleEntry vm = _virtualModules[moduleName];
        return LoadModuleCore(moduleName, vm.VirtualPath, vm.Source, vm.Sha256, options);
    }

    /// <summary>Compiles a module's entry points selected automatically by <paramref name="pipeline"/>.</summary>
    /// <param name="moduleName">The shader module name resolved through search paths.</param>
    /// <param name="pipeline">The pipeline family used to select entry points automatically.</param>
    /// <param name="options">Optional defines, specialization arguments, and ABI override.</param>
    /// <returns>The compiled shader with target code and pure-managed reflection.</returns>
    public CompiledShader Compile(string moduleName, ShaderPipelineKind pipeline = ShaderPipelineKind.All, ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        if (TryLoadFromCache(moduleName, primaryPath: null, primarySha: null, pipeline.ToString(), options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModule(moduleName, options);
        CompiledShader compiled = module.Compile(pipeline, options);
        TrySaveToCache(moduleName, primaryPath: null, primarySha: null, pipeline.ToString(), options, compiled);
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
        if (TryLoadFromCache(moduleName, primaryPath: null, primarySha: null, selectorKey, options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModule(moduleName, options);
        CompiledShader compiled = module.Compile(entryPointNames, options);
        TrySaveToCache(moduleName, primaryPath: null, primarySha: null, selectorKey, options, compiled);
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
        if (TryLoadFromCache(moduleName, primaryPath: null, primarySha: null, selectorKey, options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModule(moduleName, options);
        CompiledShader compiled = module.Compile(entryPoints, options);
        TrySaveToCache(moduleName, primaryPath: null, primarySha: null, selectorKey, options, compiled);
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
        string normPath = SlangPathUtility.NormalizePath(virtualPath);
        byte[] primarySha = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        if (TryLoadFromCache(moduleName, normPath, primarySha, pipeline.ToString(), options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModuleFromSource(moduleName, virtualPath, source, options);
        CompiledShader compiled = module.Compile(pipeline, options);
        TrySaveToCache(moduleName, normPath, primarySha, pipeline.ToString(), options, compiled);
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
        string normPath = SlangPathUtility.NormalizePath(virtualPath);
        byte[] primarySha = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        string selectorKey = BuildEntryNameKey(entryPointNames);
        if (TryLoadFromCache(moduleName, normPath, primarySha, selectorKey, options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModuleFromSource(moduleName, virtualPath, source, options);
        CompiledShader compiled = module.Compile(entryPointNames, options);
        TrySaveToCache(moduleName, normPath, primarySha, selectorKey, options, compiled);
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
        string normPath = SlangPathUtility.NormalizePath(virtualPath);
        byte[] primarySha = SHA256.HashData(Encoding.UTF8.GetBytes(source));
        string selectorKey = BuildEntryRequestKey(entryPoints);
        if (TryLoadFromCache(moduleName, normPath, primarySha, selectorKey, options, out CompiledShader? cached))
            return cached!;
        ShaderModule module = LoadModuleFromSource(moduleName, virtualPath, source, options);
        CompiledShader compiled = module.Compile(entryPoints, options);
        TrySaveToCache(moduleName, normPath, primarySha, selectorKey, options, compiled);
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
        if (TryLoadFromCache(composedKey, primaryPath: null, primarySha: null, pipeline.ToString(), options, out CompiledShader? cached))
            return cached!;
        ShaderModule template = LoadModule(templateModuleName, options);
        ShaderModule surface = LoadModule(surfaceModuleName, options);
        CompiledShader compiled = template.CompileComposed(surface, pipeline, options);
        TrySaveToCache(composedKey, primaryPath: null, primarySha: null, pipeline.ToString(), options, compiled);
        return compiled;
    }

    internal CachedModuleMetadata GetOrInspectModuleMetadata(string moduleName, ShaderCompileOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(moduleName);
        ThrowIfDisposed();

        List<(string Name, string Value)> sessionDefines = BuildSessionDefines(options?.Defines);
        string moduleKey = BuildModuleCacheKey(moduleName, sessionDefines);
        if (_moduleMetadataCache.TryGetValue(moduleKey, out CachedModuleMetadata? cached))
            return cached;

        if (_virtualModules.TryGetValue(moduleName, out VirtualModuleEntry? vm))
        {
            if (_diskCache != null &&
                _diskCache.TryReadModuleMetadata(moduleKey, dep => ResolveSourceHash(dep, vm.VirtualPath, vm.Sha256), out CachedModuleMetadata? vmMeta))
            {
                _moduleMetadataCache[moduleKey] = vmMeta;
                return vmMeta;
            }
            return CreateMetadataFromModule(LoadModuleCore(moduleName, vm.VirtualPath, vm.Source, vm.Sha256, options));
        }

        if (_diskCache != null &&
            _diskCache.TryReadModuleMetadata(moduleKey, dep => ResolveSourceHash(dep, primaryPath: null, primarySha: null), out CachedModuleMetadata? diskMeta))
        {
            _moduleMetadataCache[moduleKey] = diskMeta;
            return diskMeta;
        }

        return CreateMetadataFromModule(LoadModule(moduleName, options));
    }

    internal CachedModuleMetadata GetOrInspectSourceMetadata(string moduleName, string virtualPath, string source, ShaderCompileOptions? options = null)
    {
        RegisterVirtualModule(moduleName, virtualPath, source);
        VirtualModuleEntry vm = _virtualModules[moduleName];

        List<(string Name, string Value)> sessionDefines = BuildSessionDefines(options?.Defines);
        string moduleKey = BuildModuleCacheKey(moduleName, sessionDefines);
        if (_moduleMetadataCache.TryGetValue(moduleKey, out CachedModuleMetadata? cached))
            return cached;

        if (_diskCache != null &&
            _diskCache.TryReadModuleMetadata(moduleKey, dep => ResolveSourceHash(dep, vm.VirtualPath, vm.Sha256), out CachedModuleMetadata? diskMeta))
        {
            _moduleMetadataCache[moduleKey] = diskMeta;
            return diskMeta;
        }

        return CreateMetadataFromModule(LoadModuleCore(moduleName, vm.VirtualPath, vm.Source, vm.Sha256, options));
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

    private ShaderModule LoadModuleCore(
        string moduleName,
        string? explicitVirtualPath,
        string? explicitSource,
        byte[]? explicitSha,
        ShaderCompileOptions? options)
    {
        SessionEntry sessionEntry = GetOrCreateSession(options?.Defines);
        string? explicitShaHex = explicitSha != null ? Convert.ToHexString(explicitSha) : null;
        string moduleKey = BuildModuleCacheKey(moduleName, sessionEntry.NormalizedDefines);

        lock (sessionEntry.LoadLock)
        {
            ThrowIfDisposed();

            if (sessionEntry.LoadedModules.TryGetValue(moduleName, out ShaderModule? existing) &&
                (explicitShaHex == null || string.Equals(existing.SourceHash, explicitShaHex, StringComparison.Ordinal)))
            {
                return existing;
            }

            if (explicitSource == null)
            {
                SlangModuleHandle? alreadyLoaded = sessionEntry.Session.FindLoadedModule(moduleName);
                if (alreadyLoaded != null)
                {
                    ShaderModule wrapped = new(this, sessionEntry.Session, alreadyLoaded, options, sourceHash: null);
                    sessionEntry.LoadedModules[moduleName] = wrapped;
                    return wrapped;
                }
            }

            if (existing == null && _diskCache != null)
            {
                HashSet<string> activeModules = new(StringComparer.Ordinal);
                ShaderModule? restored = TryRestoreModuleFromIrLocked(
                    sessionEntry, moduleName, moduleKey, explicitVirtualPath, explicitSha, options, activeModules);
                if (restored != null)
                    return restored;
            }

            return LoadModuleFromSourceOrResolverLocked(
                sessionEntry, moduleName, moduleKey, explicitVirtualPath, explicitSource, explicitSha, options);
        }
    }

    private ShaderModule? TryRestoreModuleFromIrLocked(
        SessionEntry sessionEntry,
        string moduleName,
        string moduleKey,
        string? explicitVirtualPath,
        byte[]? explicitSha,
        ShaderCompileOptions? options,
        HashSet<string> activeModules)
    {
        if (_diskCache == null)
            return null;

        if (!_diskCache.TryReadModuleIr(
            moduleKey,
            dep => ResolveSourceHash(dep, explicitVirtualPath, explicitSha),
            out CachedModuleMetadata? meta,
            out byte[]? irBytes))
        {
            return null;
        }

        if (!activeModules.Add(moduleName))
            return null;

        try
        {
            for (int i = 0; i < meta.ImportedModules.Length; i++)
            {
                string impName = meta.ImportedModules[i];
                if (sessionEntry.LoadedModules.ContainsKey(impName) || sessionEntry.Session.FindLoadedModule(impName) != null)
                    continue;
                if (activeModules.Contains(impName))
                    return null;

                _virtualModules.TryGetValue(impName, out VirtualModuleEntry? impVm);
                string impKey = BuildModuleCacheKey(impName, sessionEntry.NormalizedDefines);
                ShaderModule? restoredImp = TryRestoreModuleFromIrLocked(
                    sessionEntry, impName, impKey, impVm?.VirtualPath, impVm?.Sha256, options, activeModules);
                if (restoredImp == null)
                {
                    try
                    {
                        LoadModuleFromSourceOrResolverLocked(
                            sessionEntry, impName, impKey, impVm?.VirtualPath, impVm?.Source, impVm?.Sha256, options);
                    }
                    catch (ShaderCompilationException)
                    {
                        return null;
                    }
                }
            }

            string restorePath = explicitVirtualPath ?? meta.FilePath ?? (moduleName + ".slang");
            SlangModuleHandle? handle = sessionEntry.Session.TryLoadModuleFromIRBlob(moduleName, restorePath, irBytes);
            if (handle == null)
                return null;

            string? sourceHash = explicitSha != null ? Convert.ToHexString(explicitSha) : null;
            ShaderModule restored = new(
                this,
                sessionEntry.Session,
                handle,
                options,
                sourceHash,
                meta.DependencyPaths,
                meta.ImportedModules);

            sessionEntry.LoadedModules[moduleName] = restored;
            sessionEntry.PersistedModules.Add(moduleName);
            _moduleMetadataCache[moduleKey] = meta;
            _options.Log?.Invoke($"Shader IR cache hit: '{moduleName}' ({Target}, {moduleKey[..12]}).");
            return restored;
        }
        finally
        {
            activeModules.Remove(moduleName);
        }
    }

    private ShaderModule LoadModuleFromSourceOrResolverLocked(
        SessionEntry sessionEntry,
        string moduleName,
        string moduleKey,
        string? explicitVirtualPath,
        string? explicitSource,
        byte[]? explicitSha,
        ShaderCompileOptions? options)
    {
        SlangModuleHandle handle = explicitSource != null
            ? sessionEntry.Session.LoadModuleFromSource(moduleName, explicitVirtualPath!, explicitSource)
            : sessionEntry.Session.LoadModule(moduleName);

        string? sourceHash = explicitSha != null ? Convert.ToHexString(explicitSha) : null;
        string[]? extraDeps = explicitVirtualPath != null ? [explicitVirtualPath] : null;
        ShaderModule loaded = new(this, sessionEntry.Session, handle, options, sourceHash, extraDeps);
        sessionEntry.LoadedModules[moduleName] = loaded;

        if (_diskCache != null)
        {
            PersistSessionModulesLocked(sessionEntry, moduleName, loaded, explicitVirtualPath, explicitSha);
        }
        else
        {
            _moduleMetadataCache[moduleKey] = CreateMetadataFromModule(loaded);
        }

        return loaded;
    }

    private void PersistSessionModulesLocked(
        SessionEntry sessionEntry,
        string targetLogicalName,
        ShaderModule targetModule,
        string? primaryVirtualPath,
        byte[]? primarySha)
    {
        List<SlangModuleHandle> snapshot = sessionEntry.Session.GetLoadedModulesSnapshot();
        Dictionary<string, string> pathToModule = new(StringComparer.Ordinal)
        {
            [BuiltinModuleVirtualPath] = "Core",
            [BuiltinSharedSamplerVirtualPath] = "SharedSampler",
        };
        if (primaryVirtualPath != null)
            pathToModule[primaryVirtualPath] = targetLogicalName;

        Dictionary<string, SlangModuleHandle> handlesByName = new(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.Count; i++)
        {
            SlangModuleHandle handle = snapshot[i];
            string name = handle.Name;
            if (string.IsNullOrEmpty(name))
                continue;
            handlesByName.TryAdd(name, handle);
            string? filePath = handle.FilePath;
            if (!string.IsNullOrEmpty(filePath))
                pathToModule.TryAdd(CanonicalizeVirtualOrDiskPath(filePath), name);
        }

        Dictionary<string, List<string>> moduleDepsGraph = new(StringComparer.Ordinal);
        Dictionary<string, string[]> filteredDepsByModule = new(StringComparer.Ordinal);
        for (int i = 0; i < snapshot.Count; i++)
        {
            SlangModuleHandle handle = snapshot[i];
            string name = handle.Name;
            if (string.IsNullOrEmpty(name))
                continue;

            List<string> rawPaths = handle.GetDependencyFilePaths();
            List<string> directModuleDeps = [];
            for (int r = 0; r < rawPaths.Count; r++)
            {
                string canonical = CanonicalizeVirtualOrDiskPath(rawPaths[r]);
                if (pathToModule.TryGetValue(canonical, out string? depModName) &&
                    !string.Equals(depModName, name, StringComparison.Ordinal) &&
                    !directModuleDeps.Contains(depModName, StringComparer.Ordinal))
                {
                    directModuleDeps.Add(depModName);
                }
            }
            if (sessionEntry.LoadedModules.TryGetValue(name, out ShaderModule? cachedMod))
            {
                for (int m = 0; m < cachedMod.ImportedModules.Count; m++)
                {
                    string imp = cachedMod.ImportedModules[m];
                    if (!string.Equals(imp, name, StringComparison.Ordinal) &&
                        !directModuleDeps.Contains(imp, StringComparer.Ordinal))
                    {
                        directModuleDeps.Add(imp);
                    }
                }
            }
            moduleDepsGraph[name] = directModuleDeps;

            string[] filtered = FilterDependencyPaths(rawPaths);
            if (sessionEntry.LoadedModules.TryGetValue(name, out cachedMod) && cachedMod.DependencyPaths.Count > 0)
                filtered = MergeDependencyPaths(filtered, cachedMod.DependencyPaths);
            string? ownPath = handle.FilePath;
            if (!string.IsNullOrEmpty(ownPath))
            {
                string canonicalOwn = CanonicalizeVirtualOrDiskPath(ownPath);
                if (!canonicalOwn.StartsWith(BuiltinPrefix, StringComparison.Ordinal) &&
                    !filtered.Contains(canonicalOwn, StringComparer.Ordinal))
                {
                    filtered = [.. filtered, canonicalOwn];
                }
            }
            filteredDepsByModule[name] = filtered;
        }

        for (int i = 0; i < snapshot.Count; i++)
        {
            SlangModuleHandle handle = snapshot[i];
            string name = handle.Name;
            if (string.IsNullOrEmpty(name) || handle.LoadedFromIrCache || sessionEntry.PersistedModules.Contains(name))
                continue;

            string[] orderedImports = CollectOrderedImports(name, moduleDepsGraph);
            string[] deps = filteredDepsByModule.TryGetValue(name, out string[]? d) ? d : [];
            for (int impIdx = 0; impIdx < orderedImports.Length; impIdx++)
            {
                if (sessionEntry.LoadedModules.TryGetValue(orderedImports[impIdx], out ShaderModule? impLoaded) &&
                    impLoaded.DependencyPaths.Count > 0)
                {
                    deps = MergeDependencyPaths(deps, impLoaded.DependencyPaths);
                }
                else if (filteredDepsByModule.TryGetValue(orderedImports[impIdx], out string[]? impFiltered) &&
                    impFiltered.Length > 0)
                {
                    deps = MergeDependencyPaths(deps, impFiltered);
                }
            }

            List<(string Path, byte[] Sha256)>? depHashes = TryCollectDependencyHashes(deps, primaryVirtualPath, primarySha);
            if (depHashes == null)
                continue;

            byte[]? ir = handle.Serialize();
            if (ir == null || ir.Length == 0)
                continue;

            ShaderModuleEntryPoint[] entryPoints = string.Equals(name, targetLogicalName, StringComparison.Ordinal)
                ? [.. targetModule.EntryPoints]
                : handle.GetDefinedEntryPoints();

            string? rawFilePath = handle.FilePath;
            string? canonicalFilePath = !string.IsNullOrEmpty(rawFilePath)
                ? CanonicalizeVirtualOrDiskPath(rawFilePath)
                : (string.Equals(name, targetLogicalName, StringComparison.Ordinal) ? primaryVirtualPath : null);

            CachedModuleMetadata meta = new()
            {
                ModuleName = name,
                FilePath = canonicalFilePath,
                EntryPoints = entryPoints,
                DependencyPaths = deps,
                ImportedModules = orderedImports,
            };

            string key = BuildModuleCacheKey(name, sessionEntry.NormalizedDefines);
            _diskCache!.WriteModule(key, meta, depHashes, ir);
            _moduleMetadataCache[key] = meta;
            sessionEntry.PersistedModules.Add(name);

            if (string.Equals(name, targetLogicalName, StringComparison.Ordinal))
            {
                targetModule.UpdateDependencies(deps, orderedImports);
            }
            else if (!sessionEntry.LoadedModules.ContainsKey(name))
            {
                sessionEntry.LoadedModules[name] = new ShaderModule(
                    this, sessionEntry.Session, handle, loadOptions: null, sourceHash: null, deps, orderedImports);
            }
        }
    }

    private static string[] CollectOrderedImports(string rootModule, Dictionary<string, List<string>> graph)
    {
        List<string> ordered = [];
        HashSet<string> visited = new(StringComparer.Ordinal);
        Visit(rootModule);
        return ordered.ToArray();

        void Visit(string current)
        {
            if (!visited.Add(current))
                return;
            if (graph.TryGetValue(current, out List<string>? children))
            {
                for (int i = 0; i < children.Count; i++)
                    Visit(children[i]);
            }
            if (!string.Equals(current, rootModule, StringComparison.Ordinal))
                ordered.Add(current);
        }
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
            if (normalized.StartsWith(VirtualPrefix, StringComparison.Ordinal))
                normalized = normalized[VirtualPrefix.Length..];
            if (!filtered.Contains(normalized, StringComparer.Ordinal))
                filtered.Add(normalized);
        }
        return filtered.ToArray();
    }

    private SessionEntry GetOrCreateSession(IReadOnlyList<(string Name, string Value)>? requestDefines)
    {
        List<(string Name, string Value)> merged = BuildSessionDefines(requestDefines);
        string key = BuildDefinesKey(merged);

        lock (_sessionLock)
        {
            ThrowIfDisposed();
            for (int i = 0; i < _sessions.Count; i++)
            {
                if (_sessions[i].Key == key)
                    return _sessions[i].SessionEntry;
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
            SessionEntry entry = new(created, merged);
            _sessions.Add(new SessionSlot(key, entry));
            return entry;
        }
    }

    private void RegisterVirtualAliases(string moduleName, string normPath)
    {
        string fileName = Path.GetFileName(normPath);
        if (!string.IsNullOrEmpty(fileName))
        {
            _virtualFileAliases[fileName] = normPath;
            _virtualFileAliases[fileName.Replace('_', '-')] = normPath;
        }
        _virtualFileAliases[moduleName + ".slang"] = normPath;
        _virtualFileAliases[moduleName.Replace('_', '-') + ".slang"] = normPath;
    }

    private bool TryResolveVirtualPath(string normalizedPath, out string canonicalPath)
    {
        if (normalizedPath.StartsWith(VirtualPrefix, StringComparison.Ordinal))
        {
            string sub = normalizedPath[VirtualPrefix.Length..];
            if (_inMemorySources.ContainsKey(sub))
            {
                canonicalPath = sub;
                return true;
            }
            if (_virtualFileAliases.TryGetValue(sub, out string? mapped) && _inMemorySources.ContainsKey(mapped))
            {
                canonicalPath = mapped;
                return true;
            }
            string dashedSub = sub.Replace('/', '-').Replace('_', '-');
            if (_virtualFileAliases.TryGetValue(dashedSub, out mapped) && _inMemorySources.ContainsKey(mapped))
            {
                canonicalPath = mapped;
                return true;
            }
            string dashedFile = Path.GetFileName(sub).Replace('_', '-');
            if (!string.IsNullOrEmpty(dashedFile) &&
                _virtualFileAliases.TryGetValue(dashedFile, out mapped) &&
                _inMemorySources.ContainsKey(mapped))
            {
                canonicalPath = mapped;
                return true;
            }
            canonicalPath = string.Empty;
            return false;
        }

        if (_inMemorySources.ContainsKey(normalizedPath))
        {
            canonicalPath = normalizedPath;
            return true;
        }

        canonicalPath = string.Empty;
        return false;
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

        if (TryResolveVirtualPath(norm, out string canonicalVirtual) &&
            _inMemorySources.TryGetValue(canonicalVirtual, out byte[]? inMemory))
        {
            return inMemory;
        }
        if (norm.StartsWith(VirtualPrefix, StringComparison.Ordinal))
            return null;

        if (_options.FileResolver != null)
        {
            string? resolved = _options.FileResolver(norm);
            if (resolved != null)
                return Encoding.UTF8.GetBytes(resolved);
        }

        if (File.Exists(norm))
            return File.ReadAllBytes(norm);

        if (norm.Equals(BuiltinModuleRelativePath, StringComparison.OrdinalIgnoreCase))
            return BuiltinModuleBytes;
        if (norm.Equals(BuiltinSharedSamplerRelativePath, StringComparison.OrdinalIgnoreCase))
            return BuiltinSharedSamplerBytes;

        return null;
    }

    private SlangPathKind ProbeVirtualOrDiskPath(string path)
    {
        string norm = SlangPathUtility.NormalizePath(path);
        if (norm == BuiltinRoot || norm == VirtualRoot)
            return SlangPathKind.Directory;
        if (norm == BuiltinModuleVirtualPath || norm == BuiltinSharedSamplerVirtualPath)
            return SlangPathKind.File;
        if (norm.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
            return SlangPathKind.None;

        if (TryResolveVirtualPath(norm, out _))
            return SlangPathKind.File;
        if (norm.StartsWith(VirtualPrefix, StringComparison.Ordinal))
            return SlangPathKind.None;

        if (_options.PathExists != null && _options.PathExists(norm))
            return SlangPathKind.File;
        if (_options.FileResolver != null && _options.FileResolver(norm) != null)
            return SlangPathKind.File;
        if (File.Exists(norm))
            return SlangPathKind.File;
        if (Directory.Exists(norm))
            return SlangPathKind.Directory;
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
        if (TryResolveVirtualPath(norm, out string canonicalVirtual))
            return canonicalVirtual;
        if (_options.PathExists != null && _options.PathExists(norm))
            return norm;
        if (_options.FileResolver != null && _options.FileResolver(norm) != null)
            return norm;
        if (File.Exists(norm))
            return SlangPathUtility.NormalizePath(Path.GetFullPath(norm));
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
                    "violating the Alco.Graphics bindless ABI. Move parameters, GPUBufferView, GPUTextureId, or GPUSamplerId fields into the [GPURoot] struct.",
                    target, moduleName);
            }
        }
    }

    private bool TryLoadFromCache(
        string moduleName,
        string? primaryPath,
        byte[]? primarySha,
        string selectorKey,
        ShaderCompileOptions? options,
        out CompiledShader? shader)
    {
        shader = null;
        string key = BuildProgramCacheKey(moduleName, selectorKey, options);
        string memoryKey = primarySha != null ? key + "|" + Convert.ToHexString(primarySha) : key;
        if (_compiledMemoryCache.TryGetValue(memoryKey, out CompiledShader? inMemory))
        {
            shader = inMemory;
            return true;
        }

        if (_diskCache == null)
            return false;

        if (!_diskCache.TryReadProgram(key, dep => ResolveSourceHash(dep, primaryPath, primarySha), out CompiledShader? diskShader))
            return false;

        _compiledMemoryCache[memoryKey] = diskShader;
        _options.Log?.Invoke($"Shader cache hit: '{moduleName}' ({Target}, {key[..12]}).");
        shader = diskShader;
        return true;
    }

    private void TrySaveToCache(
        string moduleName,
        string? primaryPath,
        byte[]? primarySha,
        string selectorKey,
        ShaderCompileOptions? options,
        CompiledShader shader)
    {
        string key = BuildProgramCacheKey(moduleName, selectorKey, options);
        string memoryKey = primarySha != null ? key + "|" + Convert.ToHexString(primarySha) : key;
        _compiledMemoryCache[memoryKey] = shader;

        if (_diskCache == null)
            return;

        List<(string Path, byte[] Sha256)>? depHashes = TryCollectDependencyHashes(shader.DependencyPaths, primaryPath, primarySha);
        if (depHashes == null)
            return;

        _diskCache.WriteProgram(key, depHashes, shader);
    }

    private List<(string Path, byte[] Sha256)>? TryCollectDependencyHashes(
        IReadOnlyList<string> dependencyPaths,
        string? primaryPath,
        byte[]? primarySha)
    {
        List<(string Path, byte[] Sha256)> result = new(dependencyPaths.Count + 1);
        bool includedPrimary = primaryPath == null || primarySha == null;

        for (int i = 0; i < dependencyPaths.Count; i++)
        {
            string dep = SlangPathUtility.NormalizePath(dependencyPaths[i]);
            if (dep.StartsWith(BuiltinPrefix, StringComparison.Ordinal))
                continue;

            byte[]? sha;
            if (!includedPrimary && string.Equals(dep, primaryPath, StringComparison.Ordinal))
            {
                sha = primarySha;
                includedPrimary = true;
            }
            else
            {
                sha = ResolveSourceHash(dep, primaryPath, primarySha);
            }

            if (sha == null)
                return null;
            result.Add((dep, sha));
        }

        if (!includedPrimary)
            result.Add((primaryPath!, primarySha!));

        return result;
    }

    private byte[]? ResolveSourceHash(string path, string? primaryPath, byte[]? primarySha)
    {
        string norm = SlangPathUtility.NormalizePath(path);
        if (primaryPath != null && primarySha != null && string.Equals(norm, primaryPath, StringComparison.Ordinal))
            return primarySha;

        if (norm == BuiltinModuleVirtualPath || norm.Equals(BuiltinModuleRelativePath, StringComparison.OrdinalIgnoreCase))
            return BuiltinModuleSha256;
        if (norm == BuiltinSharedSamplerVirtualPath || norm.Equals(BuiltinSharedSamplerRelativePath, StringComparison.OrdinalIgnoreCase))
            return BuiltinSharedSamplerSha256;

        if (_sourceHashCache.TryGetValue(norm, out CachedSourceHash cached))
        {
            if (cached.IsMemory || cached.IsResolver)
                return cached.Sha256;

            FileInfo info = new(norm);
            if (!info.Exists)
            {
                _sourceHashCache.TryRemove(norm, out _);
                return null;
            }
            if (info.Length == cached.FileLength && info.LastWriteTimeUtc.Ticks == cached.LastWriteUtcTicks)
                return cached.Sha256;
        }

        if (TryResolveVirtualPath(norm, out string canonicalVirtual) &&
            _inMemorySources.TryGetValue(canonicalVirtual, out byte[]? memBytes))
        {
            byte[] sha = SHA256.HashData(memBytes);
            _sourceHashCache[norm] = CachedSourceHash.ForMemory(sha);
            return sha;
        }

        if (_options.FileResolver != null)
        {
            string? resolved = _options.FileResolver(norm);
            if (resolved != null)
            {
                byte[] sha = SHA256.HashData(Encoding.UTF8.GetBytes(resolved));
                _sourceHashCache[norm] = CachedSourceHash.ForResolver(sha);
                return sha;
            }
        }

        FileInfo diskInfo = new(norm);
        if (diskInfo.Exists)
        {
            byte[] diskBytes = File.ReadAllBytes(norm);
            byte[] sha = SHA256.HashData(diskBytes);
            _sourceHashCache[norm] = CachedSourceHash.ForFile(sha, diskInfo.Length, diskInfo.LastWriteTimeUtc.Ticks);
            return sha;
        }

        return null;
    }

    private void InvalidateModuleCaches(string moduleName)
    {
        foreach (KeyValuePair<string, CachedModuleMetadata> pair in _moduleMetadataCache)
        {
            if (string.Equals(pair.Value.ModuleName, moduleName, StringComparison.Ordinal))
                _moduleMetadataCache.TryRemove(pair.Key, out _);
        }
        foreach (KeyValuePair<string, CompiledShader> pair in _compiledMemoryCache)
        {
            if (string.Equals(pair.Value.ModuleName, moduleName, StringComparison.Ordinal))
                _compiledMemoryCache.TryRemove(pair.Key, out _);
        }
    }

    private static CachedModuleMetadata CreateMetadataFromModule(ShaderModule module) => new()
    {
        ModuleName = module.Name,
        FilePath = module.FilePath,
        EntryPoints = module.EntryPoints as ShaderModuleEntryPoint[] ?? [.. module.EntryPoints],
        DependencyPaths = module.DependencyPaths as string[] ?? [.. module.DependencyPaths],
        ImportedModules = module.ImportedModules as string[] ?? [.. module.ImportedModules],
    };

    private string BuildModuleCacheKey(string moduleName, IReadOnlyList<(string Name, string Value)> sessionDefines)
    {
        StringBuilder sb = new(256);
        sb.Append("alco-graphics-mod-1|")
            .Append(CompilerVersionTag).Append('|')
            .Append((int)Target).Append('|')
            .Append(SlangCompilerOptions.GetDefaultTargetProfile(_nativeTarget)).Append('|')
            .Append((int)_options.Optimization).Append('|')
            .Append(SlangMatrixLayoutMode.RowMajor).Append("|1|")
            .Append(_builtinFingerprint).Append('|')
            .Append(moduleName).Append('|');

        AppendSortedDefines(sb, sessionDefines, includeDxilImplicitDefine: Target == ShaderTarget.Dxil);
        return ComputeTextHash(sb.ToString());
    }

    private string BuildProgramCacheKey(string moduleName, string selectorKey, ShaderCompileOptions? options)
    {
        StringBuilder sb = new(256);
        sb.Append("alco-graphics-prog-4|")
            .Append(CompilerVersionTag).Append('|')
            .Append((int)Target).Append('|')
            .Append(SlangCompilerOptions.GetDefaultTargetProfile(_nativeTarget)).Append('|')
            .Append((int)_options.Optimization).Append('|')
            .Append(SlangMatrixLayoutMode.RowMajor).Append("|1|")
            .Append(options?.ValidateAbi ?? _options.ValidateAbi).Append('|')
            .Append(_builtinFingerprint).Append('|')
            .Append(moduleName).Append('|')
            .Append(selectorKey).Append('|');

        List<(string Name, string Value)> defines = BuildSessionDefines(options?.Defines);
        AppendSortedDefines(sb, defines, includeDxilImplicitDefine: Target == ShaderTarget.Dxil);
        sb.Append('|');

        if (options != null)
        {
            for (int i = 0; i < options.SpecializationArgs.Count; i++)
                sb.Append(options.SpecializationArgs[i]).Append(',');
        }

        return ComputeTextHash(sb.ToString());
    }

    private static void AppendSortedDefines(
        StringBuilder sb,
        IReadOnlyList<(string Name, string Value)> defines,
        bool includeDxilImplicitDefine)
    {
        if (defines.Count == 0 && !includeDxilImplicitDefine)
            return;

        List<(string Name, string Value)> sorted = new(defines.Count + (includeDxilImplicitDefine ? 1 : 0));
        bool hasSvPointSize = false;
        for (int i = 0; i < defines.Count; i++)
        {
            sorted.Add(defines[i]);
            if (defines[i].Name == "SV_PointSize")
                hasSvPointSize = true;
        }
        if (includeDxilImplicitDefine && !hasSvPointSize)
            sorted.Add(("SV_PointSize", "PSIZE"));

        sorted.Sort(static (a, b) =>
        {
            int c = StringComparer.Ordinal.Compare(a.Name, b.Name);
            return c != 0 ? c : StringComparer.Ordinal.Compare(a.Value, b.Value);
        });

        for (int i = 0; i < sorted.Count; i++)
            sb.Append(sorted[i].Name).Append('=').Append(sorted[i].Value).Append(';');
    }

    private List<(string Name, string Value)> BuildSessionDefines(IReadOnlyList<(string Name, string Value)>? requestDefines)
    {
        List<(string Name, string Value)> merged = MergeDefines(_options.Defines, requestDefines);
        if (Target is ShaderTarget.MetalSource or ShaderTarget.MetalLibrary)
            AddDefineIfAbsent(merged, "ALCO_GRAPHICS_METAL", "1");
        merged.Sort(static (a, b) => StringComparer.Ordinal.Compare(a.Name, b.Name));
        return merged;
    }

    private static string BuildBuiltinFingerprint(ShaderTarget target, SlangCodeTarget nativeTarget)
    {
        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write("alco-builtin-fp-1");
            writer.Write((int)target);
            writer.Write(SlangCompilerOptions.GetDefaultTargetProfile(nativeTarget));
            writer.Write(BuiltinModuleSha256);
            writer.Write(BuiltinSharedSamplerSha256);
            if (target == ShaderTarget.Dxil)
                writer.Write(SlangCompileSession.DxilDescriptorAdapterHash);
            else if (target == ShaderTarget.SpirvCompact)
            {
                writer.Write(SlangCompileSession.VulkanCompactDescriptorAdapterHash);
                writer.Write(SpirvCompactBindings.LoweringVersion);
            }
            writer.Flush();
        }
        return ComputeBytesHash(stream.ToArray());
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
        string[] paths = new string[userPaths.Count + 2];
        for (int i = 0; i < userPaths.Count; i++)
            paths[i] = SlangPathUtility.NormalizePath(userPaths[i]);
        paths[^2] = VirtualRoot;
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
                _sessions[i].SessionEntry.Session.Dispose();
            _sessions.Clear();
        }
        _nativeCompiler.Dispose();
    }

    private sealed class VirtualModuleEntry(string moduleName, string virtualPath, string source, byte[] utf8Bytes, byte[] sha256)
    {
        public string ModuleName { get; } = moduleName;
        public string VirtualPath { get; } = virtualPath;
        public string Source { get; } = source;
        public byte[] Utf8Bytes { get; } = utf8Bytes;
        public byte[] Sha256 { get; } = sha256;
    }

    private readonly struct CachedSourceHash
    {
        public readonly byte[] Sha256;
        public readonly long FileLength;
        public readonly long LastWriteUtcTicks;
        public readonly bool IsMemory;
        public readonly bool IsResolver;

        private CachedSourceHash(byte[] sha256, long fileLength, long lastWriteUtcTicks, bool isMemory, bool isResolver)
        {
            Sha256 = sha256;
            FileLength = fileLength;
            LastWriteUtcTicks = lastWriteUtcTicks;
            IsMemory = isMemory;
            IsResolver = isResolver;
        }

        public static CachedSourceHash ForMemory(byte[] sha) => new(sha, 0, 0, isMemory: true, isResolver: false);
        public static CachedSourceHash ForResolver(byte[] sha) => new(sha, 0, 0, isMemory: false, isResolver: true);
        public static CachedSourceHash ForFile(byte[] sha, long length, long ticks) => new(sha, length, ticks, isMemory: false, isResolver: false);
    }

    private sealed class SessionEntry(SlangCompileSession session, List<(string Name, string Value)> normalizedDefines)
    {
        public readonly Lock LoadLock = new();
        public readonly SlangCompileSession Session = session;
        public readonly List<(string Name, string Value)> NormalizedDefines = normalizedDefines;
        public readonly Dictionary<string, ShaderModule> LoadedModules = new(StringComparer.Ordinal);
        public readonly HashSet<string> PersistedModules = new(StringComparer.Ordinal);
    }

    private readonly record struct SessionSlot(string Key, SessionEntry SessionEntry);
}

/// <summary>A parsed shader module handle owned by a live <see cref="ShaderCompiler"/>.</summary>
public sealed class ShaderModule
{
    private readonly ShaderCompiler _compiler;
    private readonly ShaderCompileOptions? _loadOptions;
    private string[] _dependencyPaths;
    private string[] _importedModules;

    internal SlangCompileSession Session { get; }
    internal SlangModuleHandle Handle { get; }
    internal bool LoadedFromIrCache => Handle.LoadedFromIrCache;
    internal string? SourceHash { get; }

    internal ShaderModule(
        ShaderCompiler compiler,
        SlangCompileSession session,
        SlangModuleHandle handle,
        ShaderCompileOptions? loadOptions,
        string? sourceHash = null,
        IReadOnlyList<string>? extraDependencyPaths = null,
        IReadOnlyList<string>? importedModules = null)
    {
        _compiler = compiler;
        Session = session;
        Handle = handle;
        _loadOptions = loadOptions;
        SourceHash = sourceHash;
        Name = handle.Name;
        FilePath = handle.FilePath;
        EntryPoints = handle.GetDefinedEntryPoints();

        string[] filtered = ShaderCompiler.FilterDependencyPaths(handle.GetDependencyFilePaths());
        if (extraDependencyPaths != null && extraDependencyPaths.Count > 0)
        {
            List<string> merged = new(filtered.Length + extraDependencyPaths.Count);
            for (int i = 0; i < filtered.Length; i++)
                merged.Add(filtered[i]);
            for (int i = 0; i < extraDependencyPaths.Count; i++)
            {
                string extra = SlangPathUtility.NormalizePath(extraDependencyPaths[i]);
                if (!merged.Contains(extra, StringComparer.Ordinal))
                    merged.Add(extra);
            }
            _dependencyPaths = merged.ToArray();
        }
        else
        {
            _dependencyPaths = filtered;
        }

        _importedModules = importedModules != null && importedModules.Count > 0 ? [.. importedModules] : [];
    }

    /// <summary>The module name within the compiler session.</summary>
    public string Name { get; }

    /// <summary>The resolved source path, or null when not reported.</summary>
    public string? FilePath { get; }

    /// <summary>All entry points declared in this module before target code generation.</summary>
    public IReadOnlyList<ShaderModuleEntryPoint> EntryPoints { get; }

    /// <summary>Source and transitive include paths referenced by this module, excluding built-in virtual headers.</summary>
    public IReadOnlyList<string> DependencyPaths => _dependencyPaths;

    internal IReadOnlyList<string> ImportedModules => _importedModules;

    internal void UpdateDependencies(string[] dependencyPaths, string[] importedModules)
    {
        _dependencyPaths = dependencyPaths;
        _importedModules = importedModules;
    }

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
    public ShaderPipelineKind InferPipelineKind() =>
        InferPipelineKind(Name, _compiler.Target, EntryPoints);

    internal static ShaderPipelineKind InferPipelineKind(
        string moduleName,
        ShaderTarget target,
        IReadOnlyList<ShaderModuleEntryPoint> entryPoints)
    {
        if (entryPoints.Count == 0)
        {
            throw new ShaderCompilationException(
                $"Shader module '{moduleName}' defines no [shader(...)] entry points.",
                target, moduleName);
        }

        ShaderStageKind stages = ShaderStageKind.None;
        for (int i = 0; i < entryPoints.Count; i++)
            stages |= entryPoints[i].Stage;

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
            $"Cannot infer a unique pipeline kind for module '{moduleName}' from stages [{stages}]; specify ShaderPipelineKind explicitly.",
            target, moduleName);
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

    /// <summary>Reads the declared struct and enum type layouts in this module without linking an entry point.</summary>
    /// <returns>The module's reflected type layouts.</returns>
    public IReadOnlyList<ShaderTypeLayout> GetTypes()
    {
        _compiler.ThrowIfDisposed();
        return Handle.GetTypes();
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
