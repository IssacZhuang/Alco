// Internal Slang compiler facade, session options, module loading, and per-stage native codegen.

using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Graphics.Slang;

internal sealed class SlangCompilerOptions
{
    internal static readonly SlangCompilerOptions Default = new();

    internal IReadOnlyList<string> SearchPaths { get; init; } = [];
    internal IReadOnlyList<(string Name, string Value)> PreprocessorMacros { get; init; } = [];
    internal SlangFileResolver? Resolver { get; init; }
    internal SlangPathExists? Exists { get; init; }
    internal SlangByteLoader? ByteLoader { get; init; }
    internal SlangPathProbe? PathProbe { get; init; }
    internal SlangPathCanonicalizer? PathCanonicalizer { get; init; }
    internal int OptimizationLevel { get; init; } = SlangNative.SLANG_OPTIMIZATION_LEVEL_MAXIMAL;
    internal SlangCodeTarget Target { get; init; } = SlangCodeTarget.Spirv;
    internal string? TargetProfile { get; init; }
    internal int MatrixLayoutMode { get; init; } = SlangNative.SLANG_MATRIX_LAYOUT_COLUMN_MAJOR;
    internal bool ForceCLayout { get; init; }
    internal Action<string>? Log { get; init; }

    internal string EffectiveTargetProfile =>
        TargetProfile ?? Target switch
        {
            SlangCodeTarget.Spirv or SlangCodeTarget.SpirvCompact => "spirv_1_5",
            SlangCodeTarget.Dxil => "sm_6_6",
            SlangCodeTarget.MetalLib => "metallib_2_3",
            _ => "",
        };
}

internal readonly record struct SlangEntryPointRequest(string Name, SlangShaderStage Stage);

internal readonly record struct SlangEntryPointInfo(string Name, SlangShaderStage Stage)
{
    internal (uint X, uint Y, uint Z) ThreadgroupSize { get; init; }
}

internal sealed class SlangProgram : IDisposable
{
    internal required string ModuleName { get; init; }
    internal required byte[][] EntryCode { get; init; }
    internal required SlangEntryPointInfo[] EntryPoints { get; init; }
    internal required ShaderReflection Reflection { get; init; }
    internal string? Diagnostics { get; init; }

    internal SlangComponentType? Linked { get; set; }
    internal SlangCompileSession? Owner { get; set; }

    public void Dispose()
    {
        lock (SlangCompiler.NativeLock)
        {
            Owner?.Programs.Remove(this);
            ReleaseNative();
        }
    }

    internal void ReleaseNative()
    {
        Linked?.Release();
        Linked = null;
        Owner = null;
    }
}

internal sealed class SlangCompiler : IDisposable
{
    internal static readonly Lock NativeLock = new();
    private static SlangGlobalSession? _globalSession;

    private static SlangGlobalSession GlobalSession =>
        _globalSession ??= SlangGlobalSession.Create();

    internal string BuildTag { get; }

    internal SlangCompiler()
    {
        lock (NativeLock)
            BuildTag = GlobalSession.GetBuildTagString();
    }

    internal static bool CheckTargetSupport(SlangCodeTarget target)
    {
        int nativeTarget = target switch
        {
            SlangCodeTarget.Spirv or SlangCodeTarget.SpirvCompact => SlangNative.SLANG_SPIRV,
            SlangCodeTarget.Dxil => SlangNative.SLANG_DXIL,
            SlangCodeTarget.Msl => SlangNative.SLANG_METAL,
            SlangCodeTarget.MetalLib => SlangNative.SLANG_METAL_LIB,
            _ => SlangNative.SLANG_TARGET_NONE,
        };
        lock (NativeLock)
            return GlobalSession.CheckCompileTargetSupport(nativeTarget);
    }

    internal SlangCompileSession CreateSession(SlangCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        lock (NativeLock)
            return new SlangCompileSession(GlobalSession, options);
    }

    public void Dispose()
    {
    }
}

internal sealed class SlangCompileSession : IDisposable
{
    // D3D12 shares one resource heap: buffer SRVs, buffer UAVs, then independent texture IDs. Samplers use their own heap.
    private const string DxilDescriptorAdapterSource = """
        [ForceInline]
        export T getDescriptorFromHandle<T : IOpaqueDescriptor>(DescriptorHandle<T> handleValue)
        {
            uint2 value = (uint2)handleValue;
            if (T.kind == DescriptorKind.StorageBuffer)
                value.x += T.descriptorAccess == DescriptorAccess.Read ? 0 : 65536;
            else if (T.kind != DescriptorKind.Sampler)
                value.x += 131072;
            return defaultGetDescriptorFromHandle(nonuniform(DescriptorHandle<T>(value)));
        }
        """;

    private const string VulkanCompactDescriptorAdapterSource = """
        [ForceInline]
        export T getDescriptorFromHandle<T : IOpaqueDescriptor>(DescriptorHandle<T> handleValue)
        {
            return defaultGetDescriptorFromHandle(nonuniform(handleValue), BindlessDescriptorOptions.None);
        }
        """;

    private readonly SlangGlobalSession _globalSession;
    private readonly SlangSession _session;
    private readonly SlangFileSystemExt? _fileSystem;
    private readonly SlangModule? _descriptorAdapter;
    private readonly SlangCodeTarget Target;
    private readonly int MetalLibProfile;
    private readonly int OptimizationLevel;
    private readonly Action<string>? _log;

    internal readonly List<SlangProgram> Programs = [];
    private readonly Dictionary<string, (string Source, SlangModuleHandle Handle)> _sourceModules = new(StringComparer.Ordinal);
    private bool _disposed;

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal SlangSession Native => _session;

    internal SlangCompileSession(SlangGlobalSession globalSession, SlangCompilerOptions options)
    {
        _globalSession = globalSession;
        Target = options.Target;
        OptimizationLevel = options.OptimizationLevel;
        _log = options.Log;
        if (options.ByteLoader != null && options.PathProbe != null && options.PathCanonicalizer != null)
            _fileSystem = new SlangFileSystemExt(options.ByteLoader, options.PathProbe, options.PathCanonicalizer);
        else if (options.Resolver != null)
            _fileSystem = new SlangFileSystemExt(options.Resolver, options.Exists);

        bool initialized = false;
        try
        {
            unsafe
            {
                int targetFormat = options.Target switch
                {
                    SlangCodeTarget.Spirv or SlangCodeTarget.SpirvCompact => SlangNative.SLANG_SPIRV,
                    SlangCodeTarget.Dxil => SlangNative.SLANG_DXIL,
                    SlangCodeTarget.Msl => SlangNative.SLANG_METAL,
                    SlangCodeTarget.MetalLib => SlangNative.SLANG_METAL,
                    _ => throw new ArgumentException($"Unsupported slang code target {options.Target}.", nameof(options)),
                };

                SlangTargetDesc target = SlangTargetDesc.Create(targetFormat);
                string profileName = options.EffectiveTargetProfile;
                if (Target == SlangCodeTarget.SpirvCompact && profileName != "spirv_1_5")
                    throw new ArgumentException("Vulkan Compact requires the spirv_1_5 profile.", nameof(options));
                if (profileName.Length > 0)
                {
                    target.Profile = globalSession.FindProfile(profileName);
                    if (target.Profile == SlangNative.SLANG_PROFILE_UNKNOWN)
                        throw new ArgumentException($"Unknown Slang target profile '{profileName}'.", nameof(options));
                }
                MetalLibProfile = target.Profile;

                int optionCount = 1;
                SlangCompilerOptionEntry* optionEntries = stackalloc SlangCompilerOptionEntry[5];
                optionEntries[0] = new SlangCompilerOptionEntry
                {
                    Name = SlangNative.SLANG_COMPILER_OPTION_OPTIMIZATION,
                    Value = new SlangCompilerOptionValue { IntValue0 = options.OptimizationLevel },
                };
                if (options.Target == SlangCodeTarget.Spirv)
                {
                    int descriptorHeap = globalSession.FindCapability("spvDescriptorHeapEXT");
                    int meshShading = globalSession.FindCapability("spvMeshShadingEXT");
                    if (descriptorHeap == 0 || meshShading == 0)
                        throw new ShaderCompilationException("The pinned Slang compiler lacks required SPIR-V capabilities.");
                    optionEntries[optionCount++] = new SlangCompilerOptionEntry
                    {
                        Name = SlangNative.SLANG_COMPILER_OPTION_EMIT_SPIRV_DIRECTLY,
                        Value = new SlangCompilerOptionValue { IntValue0 = 1 },
                    };
                    optionEntries[optionCount++] = new SlangCompilerOptionEntry
                    {
                        Name = SlangNative.SLANG_COMPILER_OPTION_CAPABILITY,
                        Value = new SlangCompilerOptionValue { IntValue0 = descriptorHeap },
                    };
                    optionEntries[optionCount++] = new SlangCompilerOptionEntry
                    {
                        Name = SlangNative.SLANG_COMPILER_OPTION_CAPABILITY,
                        Value = new SlangCompilerOptionValue { IntValue0 = meshShading },
                    };
                }
                else if (Target == SlangCodeTarget.SpirvCompact)
                {
                    optionEntries[optionCount++] = new SlangCompilerOptionEntry
                    {
                        Name = SlangNative.SLANG_COMPILER_OPTION_EMIT_SPIRV_DIRECTLY,
                        Value = new SlangCompilerOptionValue { IntValue0 = 1 },
                    };
                    optionEntries[optionCount++] = new SlangCompilerOptionEntry
                    {
                        Name = SlangNative.SLANG_COMPILER_OPTION_BINDLESS_SPACE_INDEX,
                        Value = new SlangCompilerOptionValue { IntValue0 = 1 },
                    };
                    optionEntries[optionCount++] = new SlangCompilerOptionEntry
                    {
                        Name = SlangNative.SLANG_COMPILER_OPTION_DEFAULT_IMAGE_FORMAT_UNKNOWN,
                        Value = new SlangCompilerOptionValue { IntValue0 = 1 },
                    };
                }
                else if (options.Target is SlangCodeTarget.Msl or SlangCodeTarget.MetalLib)
                {
                    int metal4 = globalSession.FindCapability("metallib_4_0");
                    if (metal4 == 0)
                        throw new ShaderCompilationException("The pinned Slang compiler lacks required Metal capabilities.");
                    optionEntries[optionCount++] = new SlangCompilerOptionEntry
                    {
                        Name = SlangNative.SLANG_COMPILER_OPTION_CAPABILITY,
                        Value = new SlangCompilerOptionValue { IntValue0 = metal4 },
                    };
                }
                if (options.ForceCLayout || Target == SlangCodeTarget.SpirvCompact)
                {
                    optionEntries[optionCount++] = new SlangCompilerOptionEntry
                    {
                        Name = SlangNative.SLANG_COMPILER_OPTION_FORCE_C_LAYOUT,
                        Value = new SlangCompilerOptionValue { IntValue0 = 1 },
                    };
                }
                target.CompilerOptionEntries = optionEntries;
                target.CompilerOptionEntryCount = (uint)optionCount;

                SlangPinnedUtf8[] pinnedPaths = new SlangPinnedUtf8[options.SearchPaths.Count];
                IntPtr* searchPaths = stackalloc IntPtr[options.SearchPaths.Count];
                int macroCount = options.PreprocessorMacros.Count + (options.Target == SlangCodeTarget.Dxil ? 1 : 0);
                SlangPinnedUtf8[] pinnedMacros = new SlangPinnedUtf8[macroCount * 2];
                SlangPreprocessorMacroDesc* macros = stackalloc SlangPreprocessorMacroDesc[macroCount];
                int pinnedCount = 0;
                int pinnedMacroCount = 0;
                try
                {
                    for (int i = 0; i < options.SearchPaths.Count; i++)
                    {
                        pinnedPaths[i] = new SlangPinnedUtf8(options.SearchPaths[i]);
                        pinnedCount++;
                        searchPaths[i] = pinnedPaths[i].Pointer;
                    }
                    for (int i = 0; i < macroCount; i++)
                    {
                        (string name, string value) = i < options.PreprocessorMacros.Count ? options.PreprocessorMacros[i] : ("SV_PointSize", "PSIZE");
                        pinnedMacros[i * 2] = new SlangPinnedUtf8(name);
                        pinnedMacroCount++;
                        pinnedMacros[i * 2 + 1] = new SlangPinnedUtf8(value);
                        pinnedMacroCount++;
                        macros[i] = new SlangPreprocessorMacroDesc
                        {
                            Name = pinnedMacros[i * 2].Pointer,
                            Value = pinnedMacros[i * 2 + 1].Pointer,
                        };
                    }

                    SlangSessionDesc desc = SlangSessionDesc.Create();
                    desc.Targets = &target;
                    desc.TargetCount = 1;
                    desc.DefaultMatrixLayoutMode = options.MatrixLayoutMode;
                    desc.SearchPaths = options.SearchPaths.Count > 0 ? searchPaths : null;
                    desc.SearchPathCount = options.SearchPaths.Count;
                    desc.PreprocessorMacros = macroCount > 0 ? macros : null;
                    desc.PreprocessorMacroCount = macroCount;
                    desc.FileSystem = _fileSystem?.Pointer ?? IntPtr.Zero;
                    desc.CompilerOptionEntries = optionEntries;
                    desc.CompilerOptionEntryCount = (uint)optionCount;

                    _session = globalSession.CreateSession(desc);
                }
                finally
                {
                    for (int i = 0; i < pinnedMacroCount; i++)
                        pinnedMacros[i].Dispose();
                    for (int i = 0; i < pinnedCount; i++)
                        pinnedPaths[i].Dispose();
                }
            }
            if (Target is SlangCodeTarget.Dxil or SlangCodeTarget.SpirvCompact)
            {
                string name = Target == SlangCodeTarget.Dxil ? "alco_dxil_descriptors" : "alco_vkcompact_descriptors";
                string source = Target == SlangCodeTarget.Dxil ? DxilDescriptorAdapterSource : VulkanCompactDescriptorAdapterSource;
                _descriptorAdapter = _session.LoadModuleFromSource(name, name + ".slang", System.Text.Encoding.UTF8.GetBytes(source), out string? diagnostics);
                if (_descriptorAdapter == null || HasErrors(diagnostics))
                    throw new ShaderCompilationException($"Shader compiler failed to load the {Target} descriptor adapter: {diagnostics}");
            }
            initialized = true;
        }
        finally
        {
            if (!initialized)
            {
                _session?.Release();
                _fileSystem?.Dispose();
            }
        }
    }

    internal SlangModuleHandle LoadModule(string moduleName)
    {
        lock (SlangCompiler.NativeLock)
        {
            ThrowIfDisposed();
            SlangModule? module = _session.LoadModule(moduleName, out string? diagnostics);
            if (module == null || HasErrors(diagnostics))
            {
                throw new ShaderCompilationException(
                    $"Failed to load shader module '{moduleName}': {diagnostics}",
                    ToPublicTarget(Target), moduleName, diagnostics);
            }
            LogDiagnostics(diagnostics);
            return new SlangModuleHandle(this, module);
        }
    }

    internal SlangModuleHandle LoadModuleFromSource(string moduleName, string path, string source)
    {
        lock (SlangCompiler.NativeLock)
        {
            ThrowIfDisposed();
            if (_sourceModules.TryGetValue(moduleName, out (string Source, SlangModuleHandle Handle) existing) &&
                string.Equals(existing.Source, source, StringComparison.Ordinal))
            {
                return existing.Handle;
            }
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(source);
            SlangModule? module = _session.LoadModuleFromSource(moduleName, path, bytes, out string? diagnostics);
            if (module == null || HasErrors(diagnostics))
            {
                throw new ShaderCompilationException(
                    $"Failed to parse shader module '{moduleName}' ({path}): {diagnostics}",
                    ToPublicTarget(Target), moduleName, diagnostics);
            }
            LogDiagnostics(diagnostics);
            SlangModuleHandle handle = new(this, module);
            _sourceModules[moduleName] = (source, handle);
            return handle;
        }
    }

    internal SlangProgram Compile(SlangModuleHandle module, IReadOnlyList<SlangEntryPointRequest> entryPoints)
    {
        lock (SlangCompiler.NativeLock)
        {
            ThrowIfDisposed();
            return Compile(module, entryPoints, []);
        }
    }

    internal SlangProgram Compile(SlangModuleHandle module, IReadOnlyList<SlangEntryPointRequest> entryPoints, IReadOnlyList<string> specializationArgs)
    {
        lock (SlangCompiler.NativeLock)
        {
            ThrowIfDisposed();
            module.Owner.ThrowIfDisposed();
            int entryPointOffset = _descriptorAdapter == null ? 1 : 2;
            SlangComponentType[] components = new SlangComponentType[entryPoints.Count + entryPointOffset];
            components[0] = module.Native.AsComponentType();
            if (_descriptorAdapter != null)
                components[1] = _descriptorAdapter.AsComponentType();
            try
            {
                for (int i = 0; i < entryPoints.Count; i++)
                {
                    SlangEntryPointRequest request = entryPoints[i];
                    if (Target == SlangCodeTarget.SpirvCompact && request.Stage is SlangShaderStage.Mesh or SlangShaderStage.Task)
                    {
                        throw new ShaderCompilationException(
                            $"Vulkan Compact does not support {request.Stage} entry point '{request.Name}'.",
                            ToPublicTarget(Target), module.Name);
                    }
                    SlangEntryPoint? ep = module.Native.FindAndCheckEntryPoint(request.Name, SlangStageOf(request.Stage), out string? epDiagnostics);
                    if (ep == null)
                    {
                        throw new ShaderCompilationException(
                            $"Shader entry point '{request.Name}' ({request.Stage}) not found or invalid in module '{module.Name}': {epDiagnostics}",
                            ToPublicTarget(Target), module.Name, epDiagnostics);
                    }
                    components[i + entryPointOffset] = ep.AsComponentType();
                }

                return CompileComponentsLocked(module, components, entryPoints.Count, specializationArgs, entryPoints);
            }
            finally
            {
                for (int i = entryPointOffset; i < components.Length; i++)
                    components[i]?.Release();
            }
        }
    }

    internal SlangProgram CompileAllEntryPoints(SlangModuleHandle module)
    {
        lock (SlangCompiler.NativeLock)
        {
            ThrowIfDisposed();
            module.Owner.ThrowIfDisposed();
            int count = module.Native.DefinedEntryPointCount;
            if (count == 0)
            {
                throw new ShaderCompilationException(
                    $"Shader module '{module.Name}' defines no [shader(...)] entry points.",
                    ToPublicTarget(Target), module.Name);
            }
            int entryPointOffset = _descriptorAdapter == null ? 1 : 2;
            SlangComponentType[] components = new SlangComponentType[count + entryPointOffset];
            components[0] = module.Native.AsComponentType();
            if (_descriptorAdapter != null)
                components[1] = _descriptorAdapter.AsComponentType();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    SlangEntryPoint? ep = module.Native.GetDefinedEntryPoint(i) ?? throw new ShaderCompilationException(
                        $"Shader module '{module.Name}' failed to provide entry point {i}.",
                        ToPublicTarget(Target), module.Name);
                    components[i + entryPointOffset] = ep.AsComponentType();
                }

                return CompileComponentsLocked(module, components, count, []);
            }
            finally
            {
                for (int i = entryPointOffset; i < components.Length; i++)
                    components[i]?.Release();
            }
        }
    }

    internal SlangProgram CompileComposed(
        SlangModuleHandle templateModule,
        SlangModuleHandle companionModule,
        IReadOnlyList<SlangEntryPointRequest> entryPoints,
        IReadOnlyList<string> valueSpecializationArgs)
    {
        lock (SlangCompiler.NativeLock)
        {
            ThrowIfDisposed();
            templateModule.Owner.ThrowIfDisposed();
            companionModule.Owner.ThrowIfDisposed();
            if (entryPoints.Count == 0)
            {
                throw new ShaderCompilationException(
                    $"Shader module '{templateModule.Name}' defines no entry points to compose.",
                    ToPublicTarget(Target), templateModule.Name);
            }

            bool selfComposed = templateModule.Native.NativePointer == companionModule.Native.NativePointer;
            int headerCount = 1 + (selfComposed ? 0 : 1) + (_descriptorAdapter == null ? 0 : 1);
            SlangComponentType[] components = new SlangComponentType[entryPoints.Count + headerCount];
            int compIndex = 0;
            components[compIndex++] = templateModule.Native.AsComponentType();
            if (!selfComposed)
                components[compIndex++] = companionModule.Native.AsComponentType();
            if (_descriptorAdapter != null)
                components[compIndex++] = _descriptorAdapter.AsComponentType();

            List<SlangPinnedUtf8> pins = [];
            List<SlangSpecializationArg> args = [];
            int valueIndex = 0;
            string composedName = $"{templateModule.Name}+{companionModule.Name}";
            try
            {
                for (int i = 0; i < entryPoints.Count; i++)
                {
                    SlangEntryPointRequest request = entryPoints[i];
                    if (Target == SlangCodeTarget.SpirvCompact && request.Stage is SlangShaderStage.Mesh or SlangShaderStage.Task)
                    {
                        throw new ShaderCompilationException(
                            $"Vulkan Compact does not support {request.Stage} entry point '{request.Name}'.",
                            ToPublicTarget(Target), composedName);
                    }
                    SlangEntryPoint? ep = templateModule.Native.FindAndCheckEntryPoint(request.Name, SlangStageOf(request.Stage), out string? epDiagnostics);
                    if (ep == null)
                    {
                        throw new ShaderCompilationException(
                            $"Shader entry point '{request.Name}' ({request.Stage}) not found or invalid in module '{templateModule.Name}': {epDiagnostics}",
                            ToPublicTarget(Target), composedName, epDiagnostics);
                    }
                    components[headerCount + i] = ep.AsComponentType();
                }

                int[] surfaceSlots = new int[entryPoints.Count];
                IntPtr contract = DiscoverSurfaceContract(templateModule.Name, components.AsSpan(headerCount), entryPoints, surfaceSlots,
                    out string contractName);
                IntPtr surfaceType = contract == IntPtr.Zero ? IntPtr.Zero : DiscoverSurfaceConformer(companionModule, contract, contractName);
                for (int i = 0; i < entryPoints.Count; i++)
                {
                    nint paramCount = components[headerCount + i].SpecializationParamCount;
                    for (int j = 0; j < paramCount; j++)
                    {
                        if (j == surfaceSlots[i])
                        {
                            args.Add(SlangSpecializationArg.FromType(surfaceType));
                            continue;
                        }
                        if (valueIndex >= valueSpecializationArgs.Count)
                        {
                            string entryName = entryPoints[i].Name;
                            throw new ShaderCompilationException(
                                $"Composed shader '{composedName}': entry point '{entryName}' expects more value specialization arguments than provided.",
                                ToPublicTarget(Target), composedName);
                        }
                        pins.Add(new SlangPinnedUtf8(valueSpecializationArgs[valueIndex++]));
                        args.Add(SlangSpecializationArg.FromExpr(pins[^1].Pointer));
                    }
                }

                if (valueIndex != valueSpecializationArgs.Count)
                {
                    throw new ShaderCompilationException(
                        $"Composed shader '{composedName}': {valueSpecializationArgs.Count} value specialization argument(s) provided, " +
                        $"but entry points consume {valueIndex}.",
                        ToPublicTarget(Target), composedName);
                }

                return CompileComponentsLocked(templateModule, composedName, components, entryPoints.Count, CollectionsMarshal.AsSpan(args), entryPoints);
            }
            finally
            {
                for (int i = headerCount; i < components.Length; i++)
                    components[i]?.Release();
                for (int i = 0; i < pins.Count; i++)
                    pins[i].Dispose();
            }
        }
    }

    private IntPtr DiscoverSurfaceContract(
        string moduleName, ReadOnlySpan<SlangComponentType> entries, IReadOnlyList<SlangEntryPointRequest> requests, Span<int> surfaceSlots,
        out string contractName)
    {
        contractName = string.Empty;
        IntPtr contract = IntPtr.Zero;
        surfaceSlots.Fill(-1);
        for (int i = 0; i < entries.Length; i++)
        {
            nint count = entries[i].SpecializationParamCount;
            if (count == 0)
                continue;
            string entryName = requests[i].Name;
            IntPtr layout = entries[i].GetLayout(out string? diagnostics);
            if (layout == IntPtr.Zero)
                throw new ShaderCompilationException($"Shader reflection failed for '{moduleName}.{entryName}': {diagnostics}",
                    ToPublicTarget(Target), moduleName, diagnostics);

            IntPtr generic = SlangLayoutReader.GetEntryPointGeneric(layout, 0);
            SlangGenericParameter[] parameters = SlangLayoutReader.ReadOrderedGenericParameters(generic);
            if (parameters.Length != count)
                throw new ShaderCompilationException($"Shader '{moduleName}.{entryName}' declares unsupported specialization parameters.",
                    ToPublicTarget(Target), moduleName);

            for (int j = 0; j < parameters.Length; j++)
            {
                SlangGenericParameter parameter = parameters[j];
                if (!parameter.IsType)
                    continue;
                if (surfaceSlots[i] >= 0)
                    throw new ShaderCompilationException($"Shader '{moduleName}.{entryName}' declares more than one type specialization parameter.",
                        ToPublicTarget(Target), moduleName);
                surfaceSlots[i] = j;

                IntPtr candidate = SlangNative.spReflectionGeneric_GetTypeParameterConstraintCount(generic, parameter.Variable) == 0
                    ? IntPtr.Zero : SlangNative.spReflectionGeneric_GetTypeParameterConstraintType(generic, parameter.Variable, 0);
                if (candidate == IntPtr.Zero || SlangNative.spReflectionType_GetKind(candidate) != SlangNative.SLANG_TYPE_KIND_INTERFACE)
                {
                    string? name = SlangNative.StringFromPtr(parameter.Name);
                    throw new ShaderCompilationException(
                        $"Shader '{moduleName}.{entryName}': type parameter '{name}' must be constrained by the pass contract interface.",
                        ToPublicTarget(Target), moduleName);
                }
                if (contract == IntPtr.Zero)
                {
                    contract = candidate;
                    contractName = SlangNative.StringFromPtr(SlangNative.spReflectionType_GetName(contract)) ?? "?";
                }
                else if (candidate != contract)
                {
                    string? other = SlangNative.StringFromPtr(SlangNative.spReflectionType_GetName(candidate));
                    throw new ShaderCompilationException(
                        $"Shader '{moduleName}.{entryName}' declares surface contract '{other}', different from '{contractName}'.",
                        ToPublicTarget(Target), moduleName);
                }
            }
        }
        return contract;
    }

    private IntPtr DiscoverSurfaceConformer(SlangModuleHandle companion, IntPtr contract, string contractName)
    {
        IntPtr layout = companion.Native.AsComponentType().GetLayout(out string? layoutDiagnostics);
        if (layout == IntPtr.Zero)
        {
            throw new ShaderCompilationException(
                $"Shader reflection failed for surface module '{companion.Name}': {layoutDiagnostics}",
                ToPublicTarget(Target), companion.Name, layoutDiagnostics);
        }

        List<string> candidates = [];
        IntPtr found = IntPtr.Zero;
        CollectSurfaceConformers(layout, companion.Native.GetModuleReflectionDecl(), contract, candidates, ref found, 0);

        return candidates.Count switch
        {
            1 => found,
            0 => throw new ShaderCompilationException(
                $"Surface module '{companion.Name}' declares no struct implementing contract '{contractName}'; " +
                "a surface module must export exactly one struct implementing it.",
                ToPublicTarget(Target), companion.Name),
            _ => throw new ShaderCompilationException(
                $"Surface module '{companion.Name}' declares {candidates.Count} structs implementing contract '{contractName}' " +
                $"({string.Join(", ", candidates)}); a surface module must export exactly one.",
                ToPublicTarget(Target), companion.Name),
        };
    }

    private static void CollectSurfaceConformers(IntPtr layout, IntPtr parentDecl, IntPtr contract, List<string> candidates, ref IntPtr found, int depth)
    {
        if (depth > 16 || parentDecl == IntPtr.Zero)
            return;
        uint childCount = SlangNative.spReflectionDecl_getChildrenCount(parentDecl);
        for (uint i = 0; i < childCount; i++)
        {
            IntPtr child = SlangNative.spReflectionDecl_getChild(parentDecl, i);
            if (child == IntPtr.Zero)
                continue;
            int kind = SlangNative.spReflectionDecl_getKind(child);
            if (kind == SlangNative.SLANG_DECL_KIND_NAMESPACE || kind == SlangNative.SLANG_DECL_KIND_MODULE)
            {
                CollectSurfaceConformers(layout, child, contract, candidates, ref found, depth + 1);
                continue;
            }
            if (kind != SlangNative.SLANG_DECL_KIND_STRUCT)
                continue;
            string? name = SlangNative.StringFromPtr(SlangNative.spReflectionDecl_getName(child));
            if (string.IsNullOrEmpty(name) || name.StartsWith("SLANG_", StringComparison.Ordinal))
                continue;
            IntPtr type = SlangNative.spReflection_getTypeFromDecl(child);
            if (type == IntPtr.Zero || type == contract)
                continue;
            if (SlangNative.spReflection_isSubType(layout, type, contract))
            {
                candidates.Add(name);
                found = type;
            }
        }
    }

    private SlangProgram CompileComponentsLocked(
        SlangModuleHandle module, SlangComponentType[] components, int entryCount, IReadOnlyList<string> specializationArgs,
        IReadOnlyList<SlangEntryPointRequest>? requests = null)
    {
        SlangPinnedUtf8[] pins = new SlangPinnedUtf8[specializationArgs.Count];
        SlangSpecializationArg[] args = new SlangSpecializationArg[specializationArgs.Count];
        int pinnedCount = 0;
        try
        {
            for (int i = 0; i < specializationArgs.Count; i++)
            {
                pins[i] = new SlangPinnedUtf8(specializationArgs[i]);
                pinnedCount++;
                args[i] = SlangSpecializationArg.FromExpr(pins[i].Pointer);
            }
            return CompileComponentsLocked(module, module.Name, components, entryCount, args, requests);
        }
        finally
        {
            for (int i = 0; i < pinnedCount; i++)
                pins[i].Dispose();
        }
    }

    private SlangProgram CompileComponentsLocked(
        SlangModuleHandle module,
        string programName,
        SlangComponentType[] components,
        int entryCount,
        ReadOnlySpan<SlangSpecializationArg> specializationArgs,
        IReadOnlyList<SlangEntryPointRequest>? requests)
    {
        SlangComponentType composite = _session.CreateCompositeComponentType(components, out string? compositeDiagnostics);
        SlangComponentType? specialized = null;
        string? specDiagnostics = null;
        try
        {
            SlangComponentType current = composite;
            if (specializationArgs.Length > 0)
            {
                specialized = SlangSession.TrySpecialize(current, specializationArgs, out specDiagnostics)
                    ?? throw new ShaderCompilationException($"Shader specialization failed for '{programName}': {specDiagnostics}",
                        ToPublicTarget(Target), programName, specDiagnostics);
                current = specialized;
            }

            SlangComponentType? linked = current.Link(out string? linkDiagnostics);
            try
            {
                IntPtr layout = linked.GetLayout(out string? layoutDiagnostics);
                if (layout == IntPtr.Zero)
                {
                    throw new ShaderCompilationException(
                        $"Shader layout reflection failed for '{programName}': {layoutDiagnostics}",
                        ToPublicTarget(Target), programName, layoutDiagnostics);
                }
                string? worst = FirstError(compositeDiagnostics, specDiagnostics, linkDiagnostics, layoutDiagnostics);
                if (worst != null)
                {
                    throw new ShaderCompilationException(
                        $"Shader compilation failed for '{programName}': {worst}",
                        ToPublicTarget(Target), programName, worst);
                }

                SlangEntryPointInfo[] entries = SlangLayoutReader.ReadEntryPoints(layout, programName, entryCount, requests);
                for (int i = 0; i < entries.Length; i++)
                {
                    SlangEntryPointInfo entry = entries[i];
                    if (Target == SlangCodeTarget.SpirvCompact && entry.Stage is SlangShaderStage.Mesh or SlangShaderStage.Task)
                    {
                        throw new ShaderCompilationException(
                            $"Vulkan Compact does not support {entry.Stage} entry point '{entry.Name}'.",
                            ToPublicTarget(Target), programName);
                    }
                    if ((entry.Stage is SlangShaderStage.Compute or SlangShaderStage.Mesh or SlangShaderStage.Task) &&
                        (entry.ThreadgroupSize.X == 0 || entry.ThreadgroupSize.Y == 0 || entry.ThreadgroupSize.Z == 0))
                    {
                        throw new ShaderCompilationException(
                            $"Entry '{entry.Name}' ({entry.Stage}) in module '{programName}' has no resolved threadgroup size.",
                            ToPublicTarget(Target), programName);
                    }
                }

                string? warnings = CombineDiagnostics(compositeDiagnostics, specDiagnostics, linkDiagnostics, layoutDiagnostics);
                LogDiagnostics(warnings);

                byte[][] code = new byte[entryCount][];
                for (int i = 0; i < entryCount; i++)
                {
                    code[i] = linked.GetEntryPointCode(i, out string? codeDiagnostics);
                    if (HasErrors(codeDiagnostics))
                    {
                        throw new ShaderCompilationException(
                            $"Codegen failed for entry '{entries[i].Name}' in '{programName}': {codeDiagnostics}",
                            ToPublicTarget(Target), programName, codeDiagnostics);
                    }
                    LogDiagnostics(codeDiagnostics);
                    if (Target == SlangCodeTarget.MetalLib)
                        code[i] = _globalSession.CompileMetalLibrary(code[i], entries[i].Name, SlangStageOf(entries[i].Stage),
                            MetalLibProfile, OptimizationLevel);
                    else if (Target == SlangCodeTarget.SpirvCompact)
                        code[i] = SpirvCompactBindings.Lower(code[i], $"{programName}.{entries[i].Name}");
                }

                ShaderReflection reflection = SlangLayoutReader.ReadProgramReflection(
                    layout, module.Native.GetModuleReflectionDecl(), Target, entries, programName);

                SlangProgram program = new()
                {
                    ModuleName = programName,
                    EntryCode = code,
                    EntryPoints = entries,
                    Reflection = reflection,
                    Diagnostics = warnings,
                    Linked = linked,
                    Owner = this,
                };
                Programs.Add(program);
                linked = null;
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

    internal static ShaderTarget ToPublicTarget(SlangCodeTarget target) => target switch
    {
        SlangCodeTarget.Spirv => ShaderTarget.Spirv,
        SlangCodeTarget.SpirvCompact => ShaderTarget.SpirvCompact,
        SlangCodeTarget.Dxil => ShaderTarget.Dxil,
        SlangCodeTarget.Msl => ShaderTarget.MetalSource,
        SlangCodeTarget.MetalLib => ShaderTarget.MetalLibrary,
        _ => throw new ArgumentOutOfRangeException(nameof(target)),
    };

    private static int SlangStageOf(SlangShaderStage stage) => stage switch
    {
        SlangShaderStage.Vertex => SlangNative.SLANG_STAGE_VERTEX,
        SlangShaderStage.Hull => SlangNative.SLANG_STAGE_HULL,
        SlangShaderStage.Domain => SlangNative.SLANG_STAGE_DOMAIN,
        SlangShaderStage.Geometry => SlangNative.SLANG_STAGE_GEOMETRY,
        SlangShaderStage.Pixel => SlangNative.SLANG_STAGE_FRAGMENT,
        SlangShaderStage.Compute => SlangNative.SLANG_STAGE_COMPUTE,
        SlangShaderStage.Mesh => SlangNative.SLANG_STAGE_MESH,
        SlangShaderStage.Task => SlangNative.SLANG_STAGE_AMPLIFICATION,
        _ => throw new NotSupportedException($"Shader stage {stage} cannot be compiled for a single entry point."),
    };

    internal static SlangShaderStage? MapSlangStage(int slangStage) => slangStage switch
    {
        SlangNative.SLANG_STAGE_VERTEX => SlangShaderStage.Vertex,
        SlangNative.SLANG_STAGE_HULL => SlangShaderStage.Hull,
        SlangNative.SLANG_STAGE_DOMAIN => SlangShaderStage.Domain,
        SlangNative.SLANG_STAGE_GEOMETRY => SlangShaderStage.Geometry,
        SlangNative.SLANG_STAGE_FRAGMENT => SlangShaderStage.Pixel,
        SlangNative.SLANG_STAGE_COMPUTE => SlangShaderStage.Compute,
        SlangNative.SLANG_STAGE_MESH => SlangShaderStage.Mesh,
        SlangNative.SLANG_STAGE_AMPLIFICATION => SlangShaderStage.Task,
        _ => null,
    };

    private void LogDiagnostics(string? diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(diagnostics))
            _log?.Invoke(diagnostics);
    }

    private static string? CombineDiagnostics(params string?[] blobs)
    {
        List<string>? nonEmpty = null;
        foreach (string? blob in blobs)
        {
            if (string.IsNullOrWhiteSpace(blob))
                continue;
            nonEmpty ??= [];
            nonEmpty.Add(blob.TrimEnd());
        }
        return nonEmpty == null ? null : string.Join(Environment.NewLine, nonEmpty);
    }

    private static bool HasErrors(string? diagnostics) => FirstError(diagnostics) != null;

    private static string? FirstError(params string?[] diagnosticBlobs)
    {
        foreach (string? blob in diagnosticBlobs)
        {
            if (blob == null)
                continue;
            foreach (string line in blob.Split('\n'))
            {
                ReadOnlySpan<char> trimmed = line.AsSpan().TrimStart();
                if ((trimmed.StartsWith("error", StringComparison.OrdinalIgnoreCase) &&
                     (trimmed.Length == 5 || trimmed[5] is ':' or ' ' or '[' or '(')) ||
                    line.Contains(": error", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("fatal error", StringComparison.OrdinalIgnoreCase))
                    return blob;
            }
        }
        return null;
    }

    public void Dispose()
    {
        lock (SlangCompiler.NativeLock)
        {
            if (_disposed)
                return;
            _disposed = true;
            foreach (SlangProgram program in Programs)
                program.ReleaseNative();
            Programs.Clear();
            _sourceModules.Clear();
            _session.Release();
            _fileSystem?.Dispose();
        }
    }
}

internal sealed class SlangModuleHandle
{
    internal SlangModule Native { get; }
    internal SlangCompileSession Owner { get; }

    internal string Name
    {
        get
        {
            lock (SlangCompiler.NativeLock)
            {
                Owner.ThrowIfDisposed();
                return Native.Name ?? string.Empty;
            }
        }
    }

    internal string? FilePath
    {
        get
        {
            lock (SlangCompiler.NativeLock)
            {
                Owner.ThrowIfDisposed();
                return Native.FilePath;
            }
        }
    }

    internal List<string> GetDependencyFilePaths()
    {
        lock (SlangCompiler.NativeLock)
        {
            Owner.ThrowIfDisposed();
            List<string> paths = [];
            int count = Native.DependencyFileCount;
            for (int i = 0; i < count; i++)
            {
                string? path = Native.GetDependencyFilePath(i);
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
            return paths;
        }
    }

    internal ShaderModuleEntryPoint[] GetDefinedEntryPoints()
    {
        lock (SlangCompiler.NativeLock)
        {
            Owner.ThrowIfDisposed();
            int count = Native.DefinedEntryPointCount;
            if (count == 0)
                return [];

            ShaderModuleEntryPoint[] entries = new ShaderModuleEntryPoint[count];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                SlangEntryPoint entry = Native.GetDefinedEntryPoint(i) ??
                    throw new ShaderCompilationException($"Shader module '{Name}' failed to provide entry point {i}.");
                try
                {
                    SlangComponentType component = entry.AsComponentType();
                    bool hasGenerics = component.SpecializationParamCount > 0;
                    IntPtr layout = component.GetLayout(out string? diagnostics);
                    if (layout == IntPtr.Zero)
                        throw new ShaderCompilationException($"Shader reflection failed for entry point {i} in '{Name}': {diagnostics}");
                    SlangEntryPointInfo? info = SlangLayoutReader.ReadEntryPoint(layout, 0, readThreadgroupSize: false, out _);
                    if (info.HasValue)
                    {
                        ShaderGenericParam[] generics = hasGenerics
                            ? SlangLayoutReader.ReadEntryPointGenericParameters(layout)
                            : [];
                        entries[written++] = new ShaderModuleEntryPoint(info.Value.Name, SlangLayoutReader.ToPublicStage(info.Value.Stage), hasGenerics)
                        {
                            GenericParameters = generics,
                        };
                    }
                }
                finally
                {
                    entry.Release();
                }
            }
            if (written != count)
                Array.Resize(ref entries, written);
            return entries;
        }
    }

    internal List<string> FindImplementations(string interfaceTypeName)
    {
        lock (SlangCompiler.NativeLock)
        {
            Owner.ThrowIfDisposed();
            List<string> results = [];
            IntPtr moduleDecl = Native.GetModuleReflectionDecl();
            IntPtr moduleLayout = Native.AsComponentType().GetLayout(out _);
            if (moduleDecl == IntPtr.Zero || moduleLayout == IntPtr.Zero)
                return results;

            using SlangPinnedUtf8 ifaceName = new(interfaceTypeName);
            IntPtr interfaceType = SlangNative.spReflection_FindTypeByName(moduleLayout, ifaceName.Pointer);
            if (interfaceType == IntPtr.Zero)
                return results;

            CollectImplementations(moduleLayout, moduleDecl, interfaceType, results, 0);
            return results;
        }
    }

    private static void CollectImplementations(IntPtr reflection, IntPtr parentDecl, IntPtr interfaceType, List<string> results, int depth)
    {
        if (depth > 16)
            return;
        uint count = SlangNative.spReflectionDecl_getChildrenCount(parentDecl);
        for (uint i = 0; i < count; i++)
        {
            IntPtr child = SlangNative.spReflectionDecl_getChild(parentDecl, i);
            if (child == IntPtr.Zero)
                continue;
            int kind = SlangNative.spReflectionDecl_getKind(child);
            if (kind == SlangNative.SLANG_DECL_KIND_NAMESPACE || kind == SlangNative.SLANG_DECL_KIND_MODULE)
            {
                CollectImplementations(reflection, child, interfaceType, results, depth + 1);
                continue;
            }
            if (kind != SlangNative.SLANG_DECL_KIND_STRUCT)
                continue;
            IntPtr type = SlangNative.spReflection_getTypeFromDecl(child);
            if (type == IntPtr.Zero || type == interfaceType)
                continue;
            if (SlangNative.spReflection_isSubType(reflection, type, interfaceType))
            {
                string? name = SlangNative.StringFromPtr(SlangNative.spReflectionDecl_getName(child));
                if (!string.IsNullOrEmpty(name))
                    results.Add(name);
            }
        }
    }

    internal SlangModuleHandle(SlangCompileSession owner, SlangModule module)
    {
        Owner = owner;
        Native = module;
    }
}
