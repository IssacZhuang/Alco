using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// Managed facade over the slang modern API (plan §4.1). The slang
// IGlobalSession is process-wide (see SlangCompiler); each
// SlangCompileSession owns an ISession for one (search-path set, macros,
// target options) combination.
// Sessions (and everything derived from them) are NOT thread-safe — callers
// serialize front-end operations and may then parallelize only per-entry
// code generation on a fully linked program.
//
// Compile = load module → select entry points → composite → (specialize) →
// link → ProgramLayout + per-entry target code (SPIR-V / DXIL / MSL, one
// format per session, selected by the runtime backend for wgpu's shader
// passthrough). Reflection is materialized into the engine's
// ShaderReflection by SlangReflectionReader; no target-code
// post-processing happens here.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Options describing one slang session (search paths, macros, target).</summary>
public sealed class SlangCompilerOptions
{
    public static readonly SlangCompilerOptions Default = new();

    /// <summary>Virtual search paths passed to slang ('/'-separated, relative to the file system root).</summary>
    public IReadOnlyList<string> SearchPaths { get; init; } = [];

    /// <summary>Serves module/import/include contents; when null, slang uses the OS file system.</summary>
    public SlangFileResolver? Resolver { get; init; }

    /// <summary>Optionally classifies known paths (used for unique-identity and existence checks).</summary>
    public SlangPathExists? Exists { get; init; }

    /// <summary>Optimization level (0-3); defaults to maximal.</summary>
    public int OptimizationLevel { get; init; } = SlangNative.SLANG_OPTIMIZATION_LEVEL_MAXIMAL;

    /// <summary>
    /// The code format this session emits — selected from the runtime graphics
    /// backend (Vulkan/SPIR-V, D3D12/DXIL, Metal/MSL or Metal/metallib); see
    /// <see cref="SlangCodeTarget"/>.
    /// </summary>
    public SlangCodeTarget Target { get; init; } = SlangCodeTarget.Spirv;

    /// <summary>
    /// Optional profile override for the target (e.g. "spirv_1_5", "sm_6_6").
    /// Null selects the target's pinned default: SPIR-V 1.3 (so a bundled compiler
    /// update cannot silently change the dialect), DXIL shader model 6.0 (the
    /// only level every SM6 driver guarantees) and metallib 2.3 (the oldest
    /// dialect every macOS 13 / iOS 16 runtime loads; newer OSes load it fine).
    /// MSL has no profile name in slang; an override must be null for it.
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
            SlangCodeTarget.MetalLib => "metallib_2_3",
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
    public required ShaderReflection Reflection { get; init; }
    public required IReadOnlyList<(string Name, int Stage)> EntryPoints { get; init; }

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
        };

    /// <summary>
    /// The members of the named uniform block at their post-link offsets —
    /// delegates to <see cref="ShaderReflection"/>, the single owner of the
    /// block vocabulary (empty for an unknown block; throws when the block's
    /// members do not fit the float view).
    /// </summary>
    public IReadOnlyList<ShaderUniformMember> GetUniformMembers(string cbufferName)
        => Reflection.GetUniformMembers(cbufferName);

    public void Dispose()
    {
        Owner?.NotifyProgramDisposed(this);
        Linked?.Release();
        Linked = null;
    }
}

/// <summary>
/// Stateless view over the process-wide slang global session (the slang core
/// module). The global session is created on first use and never released —
/// releasing it while any module system (or the serialized-IR caches it
/// stamped) outlives one would invalidate slang's process-wide state, and its
/// one-time cost is irrelevant next to the compiles it serves. Dispose is a
/// no-op; dispose the <see cref="SlangCompileSession"/>s instead.
/// </summary>
public sealed class SlangCompiler : IDisposable
{
    private static SlangGlobalSession? _globalSession;

    private static SlangGlobalSession GlobalSession =>
        _globalSession ??= SlangGlobalSession.Create();

    /// <summary>The pinned slang release's build tag (e.g. "2026.16..."), for cache key stamping.</summary>
    public string BuildTag { get; }

    public SlangCompiler()
    {
        BuildTag = GlobalSession.GetBuildTagString();
    }

    /// <summary>
    /// Whether slang can actually produce metallib containers on this machine.
    /// <see cref="SlangGlobalSession.CheckCompileTargetSupport"/> is not enough: it
    /// only reports whether the metallib codegen is compiled into slang, while the
    /// codegen shells out to Apple's Metal toolchain (xcrun metal on macOS, the Metal
    /// Developer Tools on Windows) and fails with error E52002 where that toolchain
    /// is absent. The honest probe is one trial compile of a minimal module; the
    /// result is cached for the process.
    /// </summary>
    public bool MetalLibSupported => _metalLibSupported ??= ProbeMetalLibSupport();

    private bool? _metalLibSupported;

    private bool ProbeMetalLibSupport()
    {
        if (!GlobalSession.CheckCompileTargetSupport(SlangNative.SLANG_METAL_LIB))
        {
            return false;
        }
        try
        {
            using SlangCompileSession session = CreateSession(new SlangCompilerOptions
            {
                Target = SlangCodeTarget.MetalLib,
            });
            SlangModuleHandle module = session.LoadModuleFromSource(
                "alco_metallib_probe", "alco_metallib_probe.slang",
                "[numthreads(1,1,1)][shader(\"compute\")] void Probe(uint3 id : SV_DispatchThreadID) {}");
            using SlangProgram program = session.Compile(module, [new SlangEntryPointRequest("Probe", ShaderStage.Compute)]);
            return program.EntryCode is [ { Length: > 4 }, .. ];
        }
        catch (Exception ex) when (ex is ShaderCompilationException or InvalidOperationException or ArgumentException)
        {
            // E52002 (pass-through compiler not found) and friends surface as
            // either exception kind; any probe failure means "no metallib here".
            return false;
        }
    }

    /// <summary>Creates a session for one (search paths, macros, options) combination.</summary>
    public SlangCompileSession CreateSession(SlangCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SlangCompileSession(GlobalSession, options);
    }

    /// <summary>No-op: the global session is process-wide and outlives every compiler.</summary>
    public void Dispose()
    {
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
                SlangCodeTarget.MetalLib => SlangNative.SLANG_METAL_LIB,
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

            SlangSessionDesc desc = SlangSessionDesc.Create();
            desc.Targets = &target;
            desc.TargetCount = 1;
            // HLSL matrix packing defaults to column-major; slang's session
            // default is row-major, so pin explicitly to match the DXC path.
            desc.DefaultMatrixLayoutMode = SlangNative.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR;
            desc.SearchPaths = options.SearchPaths.Count > 0 ? searchPaths : null;
            desc.SearchPathCount = options.SearchPaths.Count;
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
    /// module contributes the surface type. The surface type is <b>discovered, never
    /// named</b>: the template's generic entry points declare the surface contract as the
    /// constraint of their first type parameter, and the companion module must export
    /// exactly one public struct implementing that interface — checked with slang's own
    /// subtype reflection, so a renamed or mismatched implementation cannot slip through.
    /// Every generic entry point specializes with the discovered type; its value
    /// parameters (e.g. the shadow template's AlphaTest flag) consume
    /// <paramref name="valueSpecializationArgs"/> in entry order.
    /// </summary>
    /// <exception cref="ShaderCompilationException">
    /// The companion module exports no type implementing the contract, or more than one
    /// (list the candidates), or a generic entry point does not declare the contract on
    /// its first type parameter.
    /// </exception>
    public SlangProgram CompileComposed(
        SlangModuleHandle entryModule, SlangModuleHandle companionModule,
        IReadOnlyList<string> valueSpecializationArgs)
    {
        lock (_lock)
        {
            int count = entryModule.Native.DefinedEntryPointCount;
            if (count == 0)
                throw new ShaderCompilationException(
                    $"slang module '{entryModule.Name}' defines no [shader(...)] entry points.");

            // Discovery (fail-fast, before any linking): the contract interface from
            // the template's own generic declarations, the conforming type from the
            // companion's declaration tree. Both live in the session's AST for its
            // lifetime, so the pointers stay valid through Specialize below.
            IntPtr contract = DiscoverSurfaceContract(entryModule, out string contractName);
            IntPtr surfaceType = contract == IntPtr.Zero
                ? IntPtr.Zero
                : DiscoverSurfaceConformer(companionModule, contract, contractName).Type;

            SlangComponentType[] components = new SlangComponentType[count + 2];
            components[0] = entryModule.Native.AsComponentType();
            components[1] = companionModule.Native.AsComponentType();
            List<SlangPinnedUtf8> pins = [];
            List<SlangSpecializationArg> args = [];
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
                        // Every generic entry point's first specialization parameter is
                        // the surface type (DiscoverSurfaceContract enforced the
                        // constraint); the value parameters follow it.
                        args.Add(SlangSpecializationArg.FromType(surfaceType));
                        for (int j = 1; j < paramCount; j++)
                        {
                            if (valueIndex >= valueSpecializationArgs.Count)
                                throw new ShaderCompilationException(
                                    $"slang module '{entryModule.Name}': entry point {i} expects more value specialization arguments than provided.");
                            pins.Add(new SlangPinnedUtf8(valueSpecializationArgs[valueIndex]));
                            args.Add(SlangSpecializationArg.FromExpr(pins[^1].Pointer));
                            valueIndex++;
                        }
                    }
                }
                if (valueIndex != valueSpecializationArgs.Count)
                    throw new ShaderCompilationException(
                        $"slang module '{entryModule.Name}': {valueSpecializationArgs.Count} value specialization arguments provided, but the entry points consume {valueIndex}.");
                return CompileComponentsLocked(
                    $"{entryModule.Name}+{companionModule.Name}", components, count,
                    CollectionsMarshal.AsSpan(args));
            }
            finally
            {
                for (int i = 2; i < components.Length; i++)
                    components[i]?.Release();
                foreach (SlangPinnedUtf8 pinned in pins)
                    pinned.Dispose();
            }
        }
    }

    /// <summary>
    /// The surface contract of a template: the constraint of the first type parameter
    /// of its generic entry points (a generic function declaration in the module's
    /// declaration tree — generic helper types are skipped). All generic entry points
    /// must agree on one contract; a template without generic entry points links
    /// without a type argument (IntPtr.Zero).
    /// </summary>
    private static unsafe IntPtr DiscoverSurfaceContract(SlangModuleHandle template, out string contractName)
    {
        contractName = string.Empty;
        IntPtr contract = IntPtr.Zero;
        IntPtr moduleDecl = template.Native.GetModuleReflectionDecl();
        uint childCount = SlangNative.spReflectionDecl_getChildrenCount(moduleDecl);
        for (uint i = 0; i < childCount; i++)
        {
            IntPtr child = SlangNative.spReflectionDecl_getChild(moduleDecl, i);
            if (SlangNative.spReflectionDecl_getKind(child) != SlangNative.SLANG_DECL_KIND_GENERIC)
            {
                continue;
            }
            IntPtr generic = SlangNative.spReflectionDecl_castToGeneric(child);
            if (generic == IntPtr.Zero)
            {
                continue;
            }
            // Generic entry points are generic *functions*; a generic struct (e.g. a
            // Stack&lt;A, B&gt; aggregation helper) is not an entry and carries no contract.
            IntPtr inner = SlangNative.spReflectionGeneric_GetInnerDecl(generic);
            if (inner == IntPtr.Zero ||
                SlangNative.spReflectionDecl_getKind(inner) != SlangNative.SLANG_DECL_KIND_FUNC)
            {
                continue;
            }

            uint typeParamCount = SlangNative.spReflectionGeneric_GetTypeParameterCount(generic);
            if (typeParamCount == 0)
            {
                throw new ShaderCompilationException(
                    $"slang module '{template.Name}': a generic entry point declares no type parameter; " +
                    "the surface type must be the first generic parameter, constrained by the pass contract interface.");
            }
            IntPtr typeParam = SlangNative.spReflectionGeneric_GetTypeParameter(generic, 0);
            string paramName = SlangNative.StringFromPtr(SlangNative.spReflectionVariable_GetName(typeParam)) ?? "?";
            if (SlangNative.spReflectionGeneric_GetTypeParameterConstraintCount(generic, typeParam) == 0)
            {
                throw new ShaderCompilationException(
                    $"slang module '{template.Name}': type parameter '{paramName}' of a generic entry point carries no " +
                    "constraint; the first type parameter must be constrained by the pass contract interface.");
            }
            IntPtr candidate = SlangNative.spReflectionGeneric_GetTypeParameterConstraintType(generic, typeParam, 0);
            if (candidate == IntPtr.Zero ||
                SlangNative.spReflectionType_GetKind(candidate) != SlangNative.SLANG_TYPE_KIND_INTERFACE)
            {
                throw new ShaderCompilationException(
                    $"slang module '{template.Name}': the first constraint of type parameter '{paramName}' " +
                    "is not an interface; the pass contract must be an interface type.");
            }
            if (contract == IntPtr.Zero)
            {
                contract = candidate;
                contractName = SlangNative.StringFromPtr(SlangNative.spReflectionType_GetName(contract)) ?? "?";
            }
            else if (candidate != contract)
            {
                string other = SlangNative.StringFromPtr(SlangNative.spReflectionType_GetName(candidate)) ?? "?";
                throw new ShaderCompilationException(
                    $"slang module '{template.Name}': generic entry points declare different surface contracts " +
                    $"('{contractName}' and '{other}'); a pass template must have exactly one.");
            }
        }
        return contract;
    }

    /// <summary>
    /// The companion module's one type implementing the contract: its struct
    /// declarations checked with subtype reflection on the module's own layout.
    /// Zero or multiple conformers are errors — the discovery has one answer or
    /// the module is not a valid surface for this template.
    /// </summary>
    private static unsafe (string Name, IntPtr Type) DiscoverSurfaceConformer(
        SlangModuleHandle companion, IntPtr contract, string contractName)
    {
        IntPtr layout = companion.Native.AsComponentType().GetLayout(out string? layoutDiagnostics);
        if (layout == IntPtr.Zero)
            throw new ShaderCompilationException(
                $"slang getLayout failed for surface module '{companion.Name}': {layoutDiagnostics}");

        List<string> candidates = [];
        (string Name, IntPtr Type) found = (string.Empty, IntPtr.Zero);
        IntPtr moduleDecl = companion.Native.GetModuleReflectionDecl();
        uint childCount = SlangNative.spReflectionDecl_getChildrenCount(moduleDecl);
        for (uint i = 0; i < childCount; i++)
        {
            IntPtr child = SlangNative.spReflectionDecl_getChild(moduleDecl, i);
            if (SlangNative.spReflectionDecl_getKind(child) != SlangNative.SLANG_DECL_KIND_STRUCT)
            {
                continue;
            }
            string? name = SlangNative.StringFromPtr(SlangNative.spReflectionDecl_getName(child));
            // Slang synthesizes parameter-group structs (SLANG_ParameterGroup_...)
            // for cbuffer declarations — not authored surface types.
            if (string.IsNullOrEmpty(name) || name.StartsWith("SLANG_", StringComparison.Ordinal))
            {
                continue;
            }
            IntPtr type = SlangNative.spReflection_getTypeFromDecl(child);
            if (type == IntPtr.Zero)
            {
                continue;
            }
            if (SlangNative.spReflection_isSubType(layout, type, contract))
            {
                candidates.Add(name);
                found = (name, type);
            }
        }

        return candidates.Count switch
        {
            1 => found,
            0 => throw new ShaderCompilationException(
                $"surface module '{companion.Name}' declares no type implementing the pass contract " +
                $"'{contractName}'; a surface module must export exactly one public struct implementing it."),
            _ => throw new ShaderCompilationException(
                $"surface module '{companion.Name}' declares {candidates.Count} types implementing the pass " +
                $"contract '{contractName}' ({string.Join(", ", candidates)}); a surface module must export " +
                "exactly one — split the variants into separate modules or remove the unused one."),
        };
    }

    /// <summary>
    /// The module-level library reflection of a module — every uniform/parameter
    /// block it declares (with user-defined attributes and float-shaped members)
    /// and every sampled-texture slot — read from the module's own layout, no
    /// entry points, no link. Domain-neutral: attribute markers are filtered by
    /// the caller, and blocks whose members do not all fit the float view are
    /// reported, not rejected.
    /// </summary>
    public ShaderLibraryReflection GetModuleReflection(SlangModuleHandle module)
    {
        lock (_lock)
        {
            IntPtr layout = module.Native.AsComponentType().GetLayout(out string? diagnostics);
            if (layout == IntPtr.Zero)
                throw new ShaderCompilationException(
                    $"slang getLayout failed for module '{module.Name}': {diagnostics}");
            return SlangReflectionReader.BuildLibraryReflection(layout);
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
        string moduleName, SlangComponentType[] components, int entryCount,
        IReadOnlyList<string> specializationArgs)
    {
        // Expression arguments pin their UTF-8 text for the Specialize call below.
        SlangPinnedUtf8[] pins = new SlangPinnedUtf8[specializationArgs.Count];
        SlangSpecializationArg[] args = new SlangSpecializationArg[specializationArgs.Count];
        for (int i = 0; i < specializationArgs.Count; i++)
        {
            pins[i] = new SlangPinnedUtf8(specializationArgs[i]);
            args[i] = SlangSpecializationArg.FromExpr(pins[i].Pointer);
        }
        try
        {
            return CompileComponentsLocked(moduleName, components, entryCount, args);
        }
        finally
        {
            foreach (SlangPinnedUtf8 pinned in pins)
                pinned.Dispose();
        }
    }

    private unsafe SlangProgram CompileComponentsLocked(
        string moduleName, SlangComponentType[] components, int entryCount,
        ReadOnlySpan<SlangSpecializationArg> specializationArgs)
    {
        {
            SlangComponentType composite = _session.CreateCompositeComponentType(components, out string? compositeDiagnostics);
            SlangComponentType? specialized = null;
            try
            {
                SlangComponentType current = composite;
                if (specializationArgs.Length > 0)
                {
                    specialized = SlangSession.Specialize(current, specializationArgs, out string? specDiagnostics);
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

                    ShaderReflection reflection = SlangReflectionReader.BuildReflectionInfo(layout);
                    List<(string, int)> entries = SlangReflectionReader.GetEntryPoints(layout);

                    SlangProgram program = new()
                    {
                        ModuleName = moduleName,
                        EntryCode = code,
                        Reflection = reflection,
                        EntryPoints = entries,
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

    /// <summary>
    /// The module's declaration tree (a <c>DeclReflection*</c>) — the root for module-scope
    /// type discovery (walk children, take struct decls, <c>spReflection_getTypeFromDecl</c>).
    /// </summary>
    internal IntPtr GetModuleDecl() => Native.GetModuleReflectionDecl();

    internal SlangModuleHandle(SlangModule module) => Native = module;
}
