using Alco.Graphics;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Managed facade over the slang modern API (plan §4.1). One SlangCompiler
// owns the process-wide IGlobalSession; each SlangCompileSession owns an
// ISession for one (search-path set, macros, target options) combination.
// Sessions (and everything derived from them) are NOT thread-safe — callers
// serialize front-end operations and may then parallelize only per-entry
// code generation on a fully linked program.
//
// Compile = load module → select entry points → composite → (specialize) →
// link → ProgramLayout + per-entry target code (SPIR-V / DXIL / MSL, one
// format per session, selected by the runtime backend for wgpu's shader
// passthrough). Reflection is materialized into the engine's
// ShaderReflectionInfo by SlangReflectionReader; no target-code
// post-processing happens here.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Options describing one slang session (search paths, macros, target).</summary>
public sealed class SlangCompilerOptions
{
    public static readonly SlangCompilerOptions Default = new();

    /// <summary>Virtual search paths passed to slang ('/'-separated, relative to the file system root).</summary>
    public IReadOnlyList<string> SearchPaths { get; init; } = [];

    /// <summary>Session-global preprocessor macros (plan D3: transitional only).</summary>
    public IReadOnlyList<(string Name, string Value)> PreprocessorMacros { get; init; } = [];

    /// <summary>Serves module/import/include contents; when null, slang uses the OS file system.</summary>
    public SlangFileResolver? Resolver { get; init; }

    /// <summary>Optionally classifies known paths (used for unique-identity and existence checks).</summary>
    public SlangPathExists? Exists { get; init; }

    /// <summary>Optimization level (0-3); defaults to maximal.</summary>
    public int OptimizationLevel { get; init; } = SlangNative.SLANG_OPTIMIZATION_LEVEL_MAXIMAL;

    /// <summary>
    /// The code format this session emits — selected from the runtime graphics
    /// backend (Vulkan/SPIR-V, D3D12/DXIL, Metal/MSL); see <see cref="SlangCodeTarget"/>.
    /// </summary>
    public SlangCodeTarget Target { get; init; } = SlangCodeTarget.Spirv;

    /// <summary>
    /// Optional profile override for the target (e.g. "spirv_1_5", "sm_6_6").
    /// Null selects the target's pinned default: SPIR-V 1.3 (so a bundled compiler
    /// update cannot silently change the dialect) and DXIL shader model 6.0 (the
    /// only level every SM6 driver guarantees). MSL has no profile name in slang;
    /// an override must be null for it.
    /// </summary>
    public string? TargetProfile { get; init; }

    /// <summary>
    /// Optional diagnostics sink for module/program cache events (hit/miss with
    /// timings). Keeps the headless compiler free of engine logging references;
    /// null stays silent.
    /// </summary>
    public Action<string>? Log { get; init; }

    /// <summary>The effective profile name for the target (its default unless overridden).</summary>
    public string EffectiveTargetProfile =>
        TargetProfile ?? Target switch
        {
            SlangCodeTarget.Spirv => "spirv_1_3",
            SlangCodeTarget.Dxil => "sm_6_0",
            _ => "",
        };
}

/// <summary>One entry point to compile, selected by name and validated for the stage.</summary>
public readonly record struct SlangEntryPointRequest(string Name, ShaderStage Stage);

/// <summary>The result of one linked slang program: per-entry target code plus materialized reflection.</summary>
public sealed class SlangProgram : IDisposable
{
    public required string ModuleName { get; init; }
    public required byte[][] EntryCode { get; init; }
    public required ShaderReflectionInfo Reflection { get; init; }
    public required IReadOnlyList<(string Name, int Stage)> EntryPoints { get; init; }

    /// <summary>Uniform members by block name; filled by the compiler or restored from the disk cache.</summary>
    public IReadOnlyDictionary<string, List<SlangUniformMember>> UniformMembers { get; internal set; }
        = new Dictionary<string, List<SlangUniformMember>>();

    /// <summary>The native ProgramLayout; valid only while this program is alive.</summary>
    internal IntPtr NativeLayout { get; init; }

    internal SlangComponentType? Linked { get; set; }

    /// <summary>Set when the program belongs to a SlangModuleSystem (tracks native lifetime).</summary>
    internal SlangModuleSystem? Owner { get; set; }

    /// <summary>Restores a program from the disk cache — no native objects are held.</summary>
    internal static SlangProgram FromCache(string moduleName, SlangCachedProgram cached)
        => new()
        {
            ModuleName = moduleName,
            EntryCode = cached.EntryCode,
            Reflection = cached.Reflection,
            EntryPoints = cached.EntryPoints,
            UniformMembers = cached.UniformMembers,
        };

    /// <summary>The uniform members of a named block, from the program layout.</summary>
    public List<SlangUniformMember> GetUniformMembers(string cbufferName)
    {
        if (UniformMembers.TryGetValue(cbufferName, out List<SlangUniformMember>? members))
            return members;
        return NativeLayout == IntPtr.Zero ? [] : SlangReflectionReader.GetUniformMembers(NativeLayout, cbufferName);
    }

    public void Dispose()
    {
        Owner?.NotifyProgramDisposed(this);
        Linked?.Release();
        Linked = null;
    }
}

/// <summary>Owns the slang global session (the slang core module) for the process.</summary>
public sealed class SlangCompiler : IDisposable
{
    private SlangGlobalSession? _globalSession;

    /// <summary>The pinned slang release's build tag (e.g. "2026.16..."), for cache key stamping.</summary>
    public string BuildTag { get; }

    private SlangCompiler(SlangGlobalSession globalSession)
    {
        _globalSession = globalSession;
        BuildTag = globalSession.GetBuildTagString();
    }

    public static SlangCompiler Create()
    {
        return new SlangCompiler(SlangGlobalSession.Create());
    }

    /// <summary>Creates a session for one (search paths, macros, options) combination.</summary>
    public SlangCompileSession CreateSession(SlangCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SlangCompileSession(_globalSession ?? throw new ObjectDisposedException(nameof(SlangCompiler)), options);
    }

    public void Dispose()
    {
        _globalSession?.Release();
        _globalSession = null;
    }
}

/// <summary>A slang session: scope for module loading, composition, linking.</summary>
public sealed class SlangCompileSession : IDisposable
{
    private readonly SlangGlobalSession _globalSession;
    private readonly SlangSession _session;
    private readonly SlangFileSystemExt? _fileSystem;
    private readonly Lock _lock = new();

    internal SlangSession Native => _session;

    internal SlangCompileSession(SlangGlobalSession globalSession, SlangCompilerOptions options)
    {
        _globalSession = globalSession;
        _fileSystem = options.Resolver != null ? new SlangFileSystemExt(options.Resolver, options.Exists) : null;

        unsafe
        {
            int targetFormat = options.Target switch
            {
                SlangCodeTarget.Spirv => SlangNative.SLANG_SPIRV,
                SlangCodeTarget.Dxil => SlangNative.SLANG_DXIL,
                SlangCodeTarget.Msl => SlangNative.SLANG_METAL,
                _ => throw new ArgumentException($"Unsupported slang code target {options.Target}.", nameof(options)),
            };

            SlangTargetDesc target = SlangTargetDesc.Create(targetFormat);
            string profileName = options.EffectiveTargetProfile;
            if (profileName.Length > 0)
            {
                target.Profile = globalSession.FindProfile(profileName);
                if (target.Profile == SlangNative.SLANG_PROFILE_UNKNOWN)
                {
                    throw new ArgumentException(
                        $"Unknown Slang target profile '{profileName}'.", nameof(options));
                }
            }

            int optionCount = 1;
            SlangCompilerOptionEntry* optionEntries = stackalloc SlangCompilerOptionEntry[2];
            optionEntries[0] = new SlangCompilerOptionEntry
            {
                Name = SlangNative.SLANG_COMPILER_OPTION_OPTIMIZATION,
                Value = new SlangCompilerOptionValue { Kind = 0, IntValue0 = options.OptimizationLevel },
            };
            if (options.Target == SlangCodeTarget.Spirv)
            {
                // slang's direct SPIR-V emitter skips the glslang detour.
                optionEntries[1] = new SlangCompilerOptionEntry
                {
                    Name = SlangNative.SLANG_COMPILER_OPTION_EMIT_SPIRV_DIRECTLY,
                    Value = new SlangCompilerOptionValue { Kind = 0, IntValue0 = 1 },
                };
                optionCount = 2;
            }
            target.CompilerOptionEntries = optionEntries;
            target.CompilerOptionEntryCount = (uint)optionCount;

            SlangPinnedUtf8[] pinnedPaths = new SlangPinnedUtf8[options.SearchPaths.Count];
            // stackalloc needs a variable declaration with a positive count; an
            // empty array is represented by a null pointer and zero count.
            IntPtr* searchPaths = stackalloc IntPtr[Math.Max(options.SearchPaths.Count, 1)];
            for (int i = 0; i < options.SearchPaths.Count; i++)
            {
                pinnedPaths[i] = new SlangPinnedUtf8(options.SearchPaths[i]);
                searchPaths[i] = pinnedPaths[i].Pointer;
            }

            SlangPinnedUtf8[] pinnedNames = new SlangPinnedUtf8[options.PreprocessorMacros.Count];
            SlangPinnedUtf8[] pinnedValues = new SlangPinnedUtf8[options.PreprocessorMacros.Count];
            SlangPreprocessorMacroDesc* macros = stackalloc SlangPreprocessorMacroDesc[Math.Max(options.PreprocessorMacros.Count, 1)];
            for (int i = 0; i < options.PreprocessorMacros.Count; i++)
            {
                pinnedNames[i] = new SlangPinnedUtf8(options.PreprocessorMacros[i].Name);
                pinnedValues[i] = new SlangPinnedUtf8(options.PreprocessorMacros[i].Value);
                macros[i] = new SlangPreprocessorMacroDesc { Name = pinnedNames[i].Pointer, Value = pinnedValues[i].Pointer };
            }

            SlangSessionDesc desc = SlangSessionDesc.Create();
            desc.Targets = &target;
            desc.TargetCount = 1;
            // HLSL matrix packing defaults to column-major; slang's session
            // default is row-major, so pin explicitly to match the DXC path.
            desc.DefaultMatrixLayoutMode = SlangNative.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR;
            desc.SearchPaths = options.SearchPaths.Count > 0 ? searchPaths : null;
            desc.SearchPathCount = options.SearchPaths.Count;
            desc.PreprocessorMacros = options.PreprocessorMacros.Count > 0 ? macros : null;
            desc.PreprocessorMacroCount = options.PreprocessorMacros.Count;
            desc.FileSystem = _fileSystem?.Pointer ?? IntPtr.Zero;
            desc.CompilerOptionEntries = optionEntries;
            desc.CompilerOptionEntryCount = (uint)optionCount;

            _session = globalSession.CreateSession(desc);
        }
    }

    /// <summary>Loads (or returns the session-cached) module named <paramref name="moduleName"/>.</summary>
    public SlangModuleHandle LoadModule(string moduleName)
    {
        lock (_lock)
        {
            SlangModule? module = _session.LoadModule(moduleName, out string? diagnostics);
            if (module == null)
                throw new ShaderCompilationException($"slang failed to load module '{moduleName}': {diagnostics}");
            if (HasErrors(diagnostics))
                throw new ShaderCompilationException($"slang module '{moduleName}' reported errors: {diagnostics}");
            return new SlangModuleHandle(module);
        }
    }

    /// <summary>Loads a module from in-memory source; the name must be unique per content.</summary>
    public SlangModuleHandle LoadModuleFromSource(string moduleName, string path, string source)
    {
        lock (_lock)
        {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(source);
            SlangModule? module = _session.LoadModuleFromSource(moduleName, path, bytes, out string? diagnostics);
            if (module == null)
                throw new ShaderCompilationException($"slang failed to parse module '{moduleName}' ({path}): {diagnostics}");
            if (HasErrors(diagnostics))
                throw new ShaderCompilationException($"slang module '{moduleName}' ({path}) reported errors: {diagnostics}");
            return new SlangModuleHandle(module);
        }
    }

    /// <summary>Restores a module from serialized IR (see <see cref="SlangModuleHandle.Serialize"/>).</summary>
    public SlangModuleHandle LoadModuleFromIRBlob(string moduleName, string path, byte[] ir)
    {
        lock (_lock)
        {
            SlangModule? module = _session.LoadModuleFromIRBlob(moduleName, path, ir, out string? diagnostics);
            if (module == null)
                throw new ShaderCompilationException($"slang failed to load IR module '{moduleName}' ({path}): {diagnostics}");
            if (HasErrors(diagnostics))
                throw new ShaderCompilationException($"slang IR module '{moduleName}' ({path}) reported errors: {diagnostics}");
            return new SlangModuleHandle(module);
        }
    }

    /// <summary>
    /// Whether a serialized module blob is still valid for the module at <paramref name="path"/>
    /// under this session's compiler options and slang version. When the source is not visible
    /// through the session's file system the blob is accepted without validation.
    /// </summary>
    public bool IsBinaryModuleUpToDate(string path, byte[] serializedModule)
        => _session.IsBinaryModuleUpToDate(path, serializedModule);

    /// <summary>Compiles one module's requested entry points into a linked program.</summary>
    public SlangProgram Compile(SlangModuleHandle module, IReadOnlyList<SlangEntryPointRequest> entryPoints)
    {
        lock (_lock)
        {
            return Compile(module, entryPoints, []);
        }
    }

    /// <summary>Compiles with specialization arguments (generic value parameters and interface types).</summary>
    public SlangProgram Compile(SlangModuleHandle module, IReadOnlyList<SlangEntryPointRequest> entryPoints, IReadOnlyList<string> specializationArgs)
    {
        lock (_lock)
        {
            // [module, ep0, ep1, ...] — the module first keeps global parameter
            // order equal to the single-module layout; entry-point code indices
            // then follow the request order.
            SlangComponentType[] components = new SlangComponentType[entryPoints.Count + 1];
            components[0] = module.Native.AsComponentType();
            try
            {
                for (int i = 0; i < entryPoints.Count; i++)
                {
                    SlangEntryPointRequest request = entryPoints[i];
                    SlangEntryPoint? ep = module.Native.FindAndCheckEntryPoint(request.Name, SlangStageOf(request.Stage), out string? epDiagnostics);
                    if (ep == null)
                        throw new ShaderCompilationException(
                            $"slang entry point '{request.Name}' ({request.Stage}) not found or invalid in module '{module.Name}': {epDiagnostics}");
                    components[i + 1] = ep.AsComponentType();
                }

                return CompileComponentsLocked(module.Name, components, entryPoints.Count, specializationArgs);
            }
            finally
            {
                for (int i = 1; i < components.Length; i++)
                    components[i]?.Release();
            }
        }
    }

    /// <summary>
    /// Compiles every [shader(...)] entry point of <paramref name="entryModule"/> into a
    /// program composed with <paramref name="companionModule"/> — the material-composition
    /// path: the template module owns the (generic) entry points, the companion (surface)
    /// module contributes the specialization type. Convention: every generic entry point
    /// declares the surface type as its FIRST generic parameter; any value parameters
    /// follow it and consume <paramref name="valueSpecializationArgs"/> in entry order.
    /// </summary>
    public SlangProgram CompileComposed(
        SlangModuleHandle entryModule, SlangModuleHandle companionModule,
        string companionTypeName, IReadOnlyList<string> valueSpecializationArgs)
    {
        lock (_lock)
        {
            int count = entryModule.Native.DefinedEntryPointCount;
            if (count == 0)
                throw new ShaderCompilationException(
                    $"slang module '{entryModule.Name}' defines no [shader(...)] entry points.");
            SlangComponentType[] components = new SlangComponentType[count + 2];
            components[0] = entryModule.Native.AsComponentType();
            components[1] = companionModule.Native.AsComponentType();
            List<string> args = [];
            int valueIndex = 0;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    SlangEntryPoint? ep = entryModule.Native.GetDefinedEntryPoint(i);
                    if (ep == null)
                        throw new ShaderCompilationException(
                            $"slang module '{entryModule.Name}' failed to provide entry point {i}.");
                    components[i + 2] = ep.AsComponentType();

                    int paramCount = (int)ep.AsComponentType().SpecializationParamCount;
                    if (paramCount > 0)
                    {
                        args.Add(companionTypeName);
                        for (int j = 1; j < paramCount; j++)
                        {
                            if (valueIndex >= valueSpecializationArgs.Count)
                                throw new ShaderCompilationException(
                                    $"slang module '{entryModule.Name}': entry point {i} expects more value specialization arguments than provided.");
                            args.Add(valueSpecializationArgs[valueIndex++]);
                        }
                    }
                }
                if (valueIndex != valueSpecializationArgs.Count)
                    throw new ShaderCompilationException(
                        $"slang module '{entryModule.Name}': {valueSpecializationArgs.Count} value specialization arguments provided, but the entry points consume {valueIndex}.");
                return CompileComponentsLocked(
                    $"{entryModule.Name}+{companionModule.Name}", components, count, args);
            }
            finally
            {
                for (int i = 2; i < components.Length; i++)
                    components[i]?.Release();
            }
        }
    }

    /// <summary>
    /// The members of a module's named uniform block, read from the module's own layout —
    /// no entry points, no link (the material-parameter probe). Empty when the module
    /// declares no such block.
    /// </summary>
    public List<SlangUniformMember> GetModuleUniformMembers(SlangModuleHandle module, string cbufferName)
    {
        lock (_lock)
        {
            IntPtr layout = module.Native.AsComponentType().GetLayout(out string? diagnostics);
            if (layout == IntPtr.Zero)
                throw new ShaderCompilationException(
                    $"slang getLayout failed for module '{module.Name}': {diagnostics}");
            return SlangReflectionReader.GetUniformMembers(layout, cbufferName);
        }
    }

    /// <summary>
    /// Every uniform block of a module carrying the given user-defined attribute (e.g.
    /// <c>[MaterialParams]</c>), read from the module's own layout — no entry points,
    /// no link. The material-parameter discovery probe: blocks are found by the marker,
    /// not by a fixed name, so a surface names and splits its parameter blocks freely.
    /// </summary>
    public List<(string BlockName, List<SlangUniformMember> Members)> GetModuleMarkedUniformBlocks(
        SlangModuleHandle module, string attributeName)
    {
        lock (_lock)
        {
            IntPtr layout = module.Native.AsComponentType().GetLayout(out string? diagnostics);
            if (layout == IntPtr.Zero)
                throw new ShaderCompilationException(
                    $"slang getLayout failed for module '{module.Name}': {diagnostics}");
            return SlangReflectionReader.GetMarkedUniformBlocks(layout, attributeName);
        }
    }

    /// <summary>
    /// Compiles every [shader(...)] entry point the module defines, in definition order —
    /// callers that don't know entry names up front (module-name keyed lookups).
    /// </summary>
    public SlangProgram CompileAllEntryPoints(SlangModuleHandle module, IReadOnlyList<string> specializationArgs)
    {
        lock (_lock)
        {
            int count = module.Native.DefinedEntryPointCount;
            if (count == 0)
                throw new ShaderCompilationException(
                    $"slang module '{module.Name}' defines no [shader(...)] entry points.");
            SlangComponentType[] components = new SlangComponentType[count + 1];
            components[0] = module.Native.AsComponentType();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    SlangEntryPoint? ep = module.Native.GetDefinedEntryPoint(i);
                    if (ep == null)
                        throw new ShaderCompilationException(
                            $"slang module '{module.Name}' failed to provide entry point {i}.");
                    components[i + 1] = ep.AsComponentType();
                }
                return CompileComponentsLocked(module.Name, components, count, specializationArgs);
            }
            finally
            {
                for (int i = 1; i < components.Length; i++)
                    components[i]?.Release();
            }
        }
    }

    private unsafe SlangProgram CompileComponentsLocked(
        string moduleName, SlangComponentType[] components, int entryCount, IReadOnlyList<string> specializationArgs)
    {
        {
            SlangComponentType composite = _session.CreateCompositeComponentType(components, out string? compositeDiagnostics);
            SlangComponentType? specialized = null;
            try
            {
                SlangComponentType current = composite;
                if (specializationArgs.Count > 0)
                {
                    SlangPinnedUtf8[] pinnedArgs = new SlangPinnedUtf8[specializationArgs.Count];
                    SlangSpecializationArg[] args = new SlangSpecializationArg[specializationArgs.Count];
                    for (int i = 0; i < specializationArgs.Count; i++)
                    {
                        pinnedArgs[i] = new SlangPinnedUtf8(specializationArgs[i]);
                        args[i] = SlangSpecializationArg.FromExpr(pinnedArgs[i].Pointer);
                    }
                    try
                    {
                        specialized = SlangSession.Specialize(current, args, out string? specDiagnostics);
                    }
                    finally
                    {
                        foreach (SlangPinnedUtf8 pinned in pinnedArgs)
                            pinned.Dispose();
                    }
                    current = specialized;
                }

                SlangComponentType? linked = current.Link(out string? linkDiagnostics);
                try
                {
                    IntPtr layout = linked.GetLayout(out string? layoutDiagnostics);
                    if (layout == IntPtr.Zero)
                        throw new ShaderCompilationException(
                            $"slang getLayout failed for '{moduleName}': {layoutDiagnostics}");
                    string? worst = FirstError(compositeDiagnostics, linkDiagnostics, layoutDiagnostics);
                    if (worst != null)
                        throw new ShaderCompilationException($"slang reported errors for '{moduleName}': {worst}");

                    byte[][] code = new byte[entryCount][];
                    for (int i = 0; i < entryCount; i++)
                        code[i] = linked.GetEntryPointCode(i, out _);

                    ShaderReflectionInfo reflection = SlangReflectionReader.BuildReflectionInfo(layout);
                    List<(string, int)> entries = SlangReflectionReader.GetEntryPoints(layout);

                    SlangProgram program = new()
                    {
                        ModuleName = moduleName,
                        EntryCode = code,
                        Reflection = reflection,
                        EntryPoints = entries,
                        NativeLayout = layout,
                        Linked = linked,
                    };
                    linked = null; // ownership transferred to the program
                    return program;
                }
                finally
                {
                    linked?.Release();
                }
            }
            finally
            {
                specialized?.Release();
                composite.Release();
            }
        }
    }

    private static int SlangStageOf(ShaderStage stage)
    {
        return stage switch
        {
            ShaderStage.Vertex => SlangNative.SLANG_STAGE_VERTEX,
            ShaderStage.Fragment => SlangNative.SLANG_STAGE_FRAGMENT,
            ShaderStage.Compute => SlangNative.SLANG_STAGE_COMPUTE,
            ShaderStage.Hull => SlangNative.SLANG_STAGE_HULL,
            ShaderStage.Domain => SlangNative.SLANG_STAGE_DOMAIN,
            ShaderStage.Geometry => SlangNative.SLANG_STAGE_GEOMETRY,
            _ => throw new NotSupportedException($"Shader stage {stage} cannot be compiled for a single entry point."),
        };
    }

    /// <summary>Converts a slang stage id from a program layout into the engine's stage flags.</summary>
    public static ShaderStage SlangStageToEngine(int slangStage)
    {
        return slangStage switch
        {
            SlangNative.SLANG_STAGE_VERTEX => ShaderStage.Vertex,
            SlangNative.SLANG_STAGE_HULL => ShaderStage.Hull,
            SlangNative.SLANG_STAGE_DOMAIN => ShaderStage.Domain,
            SlangNative.SLANG_STAGE_GEOMETRY => ShaderStage.Geometry,
            SlangNative.SLANG_STAGE_FRAGMENT => ShaderStage.Fragment,
            SlangNative.SLANG_STAGE_COMPUTE => ShaderStage.Compute,
            _ => throw new NotSupportedException($"Unknown slang stage id {slangStage}."),
        };
    }

    private static bool HasErrors(string? diagnostics)
        => FirstError(diagnostics) != null;

    private static string? FirstError(params string?[] diagnosticBlobs)
    {
        foreach (string? blob in diagnosticBlobs)
        {
            if (blob == null)
                continue;
            foreach (string line in blob.Split('\n'))
            {
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
                    return blob;
            }
        }
        return null;
    }

    public void Dispose()
    {
        _session.Release();
        _fileSystem?.Dispose();
    }
}

/// <summary>A handle to a loaded module; alive for the session's lifetime.</summary>
public sealed class SlangModuleHandle
{
    internal SlangModule Native { get; }
    public string Name => Native.Name ?? string.Empty;
    public string? FilePath => Native.FilePath;

    /// <summary>File paths this module depends on (its own source plus every transitively included file).</summary>
    public List<string> GetDependencyFilePaths()
    {
        List<string> paths = [];
        int count = Native.DependencyFileCount;
        for (int i = 0; i < count; i++)
        {
            string? path = Native.GetDependencyFilePath(i);
            if (path != null)
                paths.Add(path);
        }
        return paths;
    }

    /// <summary>
    /// The module's [shader(...)] entry point count and whether any entry point
    /// declares generic parameters — (0, false) for import-only libraries. The
    /// front-end has already checked every entry (and every branch of its
    /// generic bodies) at module load; a module with no generic entry links
    /// unspecialized, a generic one needs its arguments at link time.
    /// </summary>
    public (int Count, bool AnyGeneric) GetEntryPointInfo()
    {
        int count = Native.DefinedEntryPointCount;
        bool anyGeneric = false;
        for (int i = 0; i < count && !anyGeneric; i++)
        {
            SlangEntryPoint? entry = Native.GetDefinedEntryPoint(i);
            if (entry != null && entry.AsComponentType().SpecializationParamCount > 0)
            {
                anyGeneric = true;
            }
        }
        return (count, anyGeneric);
    }

    /// <summary>The module's serialized IR blob, or null when serialization fails.</summary>
    public byte[]? Serialize() => Native.Serialize();

    internal SlangModuleHandle(SlangModule module) => Native = module;
}
