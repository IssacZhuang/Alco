using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using Alco.Graphics;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// SlangModuleSystem (plan §4.2, headless core): owns the slang compile session,
// the module cache, the dependency graph with reverse invalidation, and the two
// disk-cache layers that replace ShaderCache:
//
//   (a) modules/<hash>.slang-module + .meta — serialized module IR. The meta
//       sidecar stamps the slang build tag, the session's code target, the
//       hash of the module's EXACT source (the resolved file content) and
//       every FILE dependency's content hash. The module's own path identity
//       is deliberately not a recorded dependency: an extension-less module
//       name resolves to nothing through the file resolver, and validating it
//       through it made every cache read miss. For the same reason
//       isBinaryModuleUpToDate is bypassed — it accepts source-less blobs
//       without validation (the plan's explicit caveat).
//   (b) programs/<hash>.bin — linked programs (per-entry target code +
//       materialized reflection + uniform members), keyed by module IR hash,
//       entry set, specialization, code target and build tag.
//
// Invalidation rebuilds the compile session: slang caches imported modules
// inside the session and modules are immutable, so a changed lib can only be
// observed through a fresh session (the process-wide global session stays
// valid — see SlangCompiler). Unaffected modules reload from the IR disk
// cache. The Alco.Rendering ShaderSystem wraps this class and turns
// ModulesInvalidated into Shader version bumps; nothing here touches GPU types.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Owns slang modules, their dependency graph and the shader disk caches.</summary>
public sealed class SlangModuleSystem : IDisposable
{
    private const int MetaVersion = 3;
    private const int ProgramCacheKeyVersion = 4;

    private readonly SlangCompilerOptions _options;
    private readonly string? _cacheDirectory;
    private readonly Lock _lock = new();

    // Immutable for the system's lifetime: the global slang session is
    // process-wide (see SlangCompiler); only the per-system compile session is
    // rebuilt on invalidation.
    private readonly SlangCompiler _compiler = new();
    private SlangCompileSession _session = null!;

    private readonly Dictionary<string, ModuleEntry> _modules = new();
    private readonly Dictionary<string, HashSet<string>> _fileToModules = new(); // file → module names
    private readonly Dictionary<string, SlangProgram> _programs = new();         // cache key → program
    private readonly List<SlangProgram> _livePrograms = [];
    private readonly HashSet<string> _rootLoadedPaths = new(StringComparer.OrdinalIgnoreCase);

    // Virtual module sources, resolved by name ahead of the file resolver:
    // generated wrapper modules (e.g. tests registering embedded sources).
    // Sources survive session rebuilds — they are inputs, like files.
    private readonly ConcurrentDictionary<string, string> _virtualSources = new(StringComparer.Ordinal);

    /// <summary>The pinned slang release's build tag — part of every cache key.</summary>
    public string BuildTag { get; }

    /// <summary>The code format this system's session emits (part of every cache key).</summary>
    public SlangCodeTarget Target => _options.Target;

    /// <summary>Raised after modules were dropped due to source changes; carries the affected module names.</summary>
    public event Action<IReadOnlyList<string>>? ModulesInvalidated;

    /// <param name="options">Session options (resolver doubles as the content source for staleness hashing).</param>
    /// <param name="cacheDirectory">Disk-cache root; null disables all disk caching.</param>
    public SlangModuleSystem(SlangCompilerOptions options, string? cacheDirectory)
    {
        _options = options;
        _cacheDirectory = cacheDirectory;
        if (cacheDirectory != null)
        {
            Directory.CreateDirectory(Path.Combine(cacheDirectory, "modules"));
            Directory.CreateDirectory(Path.Combine(cacheDirectory, "programs"));
        }
        CreateSession();
        BuildTag = _compiler.BuildTag;
    }

    private void CreateSession()
    {
        // Virtual modules resolve ahead of the asset resolver so a wrapper's
        // import-by-name reaches generated modules.
        SlangCompilerOptions sessionOptions = _options.Resolver == null
            ? _options
            : new SlangCompilerOptions
            {
                SearchPaths = _options.SearchPaths,
                Resolver = ResolveWithVirtualSources,
                Exists = _options.Exists,
                OptimizationLevel = _options.OptimizationLevel,
                Target = _options.Target,
                TargetProfile = _options.TargetProfile,
            };
        _session = _compiler.CreateSession(sessionOptions);
    }

    /// <summary>
    /// Registers a virtual module source: the name resolves to the source before the file
    /// resolver is consulted, so other modules can <c>import</c> it by name. Registering does
    /// not load the module — pair with <see cref="GetOrLoadModule(string, string, string)"/>
    /// to give it a real path identity in the dependency graph.
    /// </summary>
    public void AddVirtualModule(string name, string source)
    {
        _virtualSources[name] = source;
    }

    private string? ResolveWithVirtualSources(string path)
    {
        if (_virtualSources.TryGetValue(SlangPathUtility.NormalizePath(path), out string? source))
        {
            return source;
        }
        return _options.Resolver!(path);
    }

    /// <summary>Loads a module from virtual source (the engine's asset system is the file provider).</summary>
    public SlangModuleHandle GetOrLoadModule(string moduleName, string path, string source)
    {
        lock (_lock)
        {
            if (_modules.TryGetValue(moduleName, out ModuleEntry? existing))
                return existing.Module;
            ModuleEntry entry = LoadModuleLocked(moduleName, path, source);
            _modules[moduleName] = entry;
            IndexDependenciesLocked(moduleName, entry);
            return entry.Module;
        }
    }

    /// <summary>
    /// Loads a module by name through the session's search paths / resolver.
    /// Variant axes are expressed as generic value specializations at link
    /// time (see GetProgram's specialization arguments), never as
    /// preprocessor permutations.
    /// </summary>
    public SlangModuleHandle GetOrLoadModule(string moduleName)
    {
        lock (_lock)
        {
            if (_modules.TryGetValue(moduleName, out ModuleEntry? existing))
                return existing.Module;

            // The disk cache is keyed by content hashes served through the
            // resolver; without one there is nothing to hash — load directly.
            if (_options.Resolver == null)
            {
                SlangModuleHandle module = _session.LoadModule(moduleName);
                ModuleEntry raw = new()
                {
                    LogicalName = moduleName,
                    Module = module,
                    Dependencies = [.. module.GetDependencyFilePaths().Select(SlangPathUtility.NormalizePath)],
                };
                _modules[moduleName] = raw;
                IndexDependenciesLocked(moduleName, raw);
                return module;
            }

            // Probe the module's source through the name→file conventions the
            // resolver implements, then load from that source. The candidate
            // that hit becomes the module's path identity: slang reports it
            // back as a dependency path and the disk cache validates those
            // through the resolver — the extension-less module name used as
            // the identity before resolves to nothing, so writes succeeded
            // while every read missed and each run re-parsed the module.
            if (ResolveModuleSource(moduleName) is not { } probe)
                throw new ShaderCompilationException($"slang module '{moduleName}' not found through the resolver.");
            ModuleEntry entry = LoadModuleLocked(moduleName, probe.Candidate, probe.Source);
            _modules[moduleName] = entry;
            IndexDependenciesLocked(moduleName, entry);
            return entry.Module;
        }
    }

    /// <summary>Every file the module (transitively) depends on, normalized; empty when not loaded.</summary>
    public IReadOnlyList<string> GetModuleDependencies(string moduleName)
    {
        lock (_lock)
        {
            return _modules.TryGetValue(moduleName, out ModuleEntry? entry)
                ? entry.Dependencies
                : [];
        }
    }

    /// <summary>Whether the module was restored from serialized IR instead of parsing sources.</summary>
    public bool IsModuleLoadedFromCache(string moduleName)
    {
        lock (_lock)
        {
            return _modules.TryGetValue(moduleName, out ModuleEntry? entry) && entry.LoadedFromIR;
        }
    }

    /// <summary>The names of all currently loaded modules.</summary>
    public IReadOnlyList<string> GetLoadedModuleNames()
    {
        lock (_lock)
        {
            return [.. _modules.Values.Select(entry => entry.LogicalName).Distinct()];
        }
    }

    /// <summary>Whether a module of this name is loaded — including source-registered
    /// modules, which resolver probing alone cannot discover.</summary>
    public bool IsModuleLoaded(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        lock (_lock)
        {
            return _modules.ContainsKey(moduleName);
        }
    }

    /// <summary>
    /// Links (or restores) one program of a loaded module. <paramref name="entryPoints"/> order
    /// defines the EntryCode order.
    /// </summary>
    public SlangProgram GetProgram(
        string moduleName, IReadOnlyList<SlangEntryPointRequest> entryPoints, IReadOnlyList<string> specializationArgs)
    {
        lock (_lock)
        {
            if (!_modules.TryGetValue(moduleName, out ModuleEntry? entry))
                throw new InvalidOperationException(
                    $"Module '{moduleName}' is not loaded; call GetOrLoadModule first.");

            string entriesKey = string.Join(";", entryPoints.Select(request => $"{request.Name}:{(int)request.Stage}"));
            return GetProgramLocked(moduleName, entry, entriesKey,
                specArgs => _session.Compile(entry.Module, entryPoints, specArgs), specializationArgs);
        }
    }

    /// <summary>
    /// Links (or restores) the program of every [shader(...)] entry point the module defines,
    /// in definition order — the module-name keyed lookup path.
    /// </summary>
    public SlangProgram GetProgramAllEntries(
        string moduleName, IReadOnlyList<string> specializationArgs)
    {
        lock (_lock)
        {
            if (!_modules.TryGetValue(moduleName, out ModuleEntry? entry))
                throw new InvalidOperationException(
                    $"Module '{moduleName}' is not loaded; call GetOrLoadModule first.");

            return GetProgramLocked(moduleName, entry, "all",
                specArgs => _session.CompileAllEntryPoints(entry.Module, specArgs), specializationArgs);
        }
    }

    /// <summary>
    /// Links (or restores) a composed program: the template module owns the (generic)
    /// [shader(...)] entry points, the companion (surface) module contributes the
    /// surface type — the material-composition path, which needs no generated
    /// wrapper module. The surface type is discovered from the modules themselves
    /// (see <see cref="SlangCompileSession.CompileComposed"/>): the template's entry
    /// points declare the contract, the companion must export exactly one conforming
    /// type — no type name is passed or configured. Both modules load through
    /// <see cref="GetOrLoadModule(string)"/>. Value specialization arguments feed the
    /// entries' value parameters in entry order (e.g. the shadow template's
    /// AlphaTest flag).
    /// </summary>
    public SlangProgram GetComposedProgram(
        string templateModuleName, string companionModuleName,
        IReadOnlyList<string> valueSpecializationArgs)
    {
        SlangModuleHandle template = GetOrLoadModule(templateModuleName);
        SlangModuleHandle companion = GetOrLoadModule(companionModuleName);
        lock (_lock)
        {
            ModuleEntry templateEntry = _modules[templateModuleName];
            ModuleEntry companionEntry = _modules[companionModuleName];

            string logicalName = $"{templateEntry.LogicalName}+{companionEntry.LogicalName}";
            // The surface type is a pure function of the two modules' contents
            // (contract × conformer), which the IR hashes pin — the key needs no
            // type name.
            string key = ComposedProgramCacheKey(
                templateModuleName, templateEntry.IrHash,
                companionModuleName, companionEntry.IrHash,
                valueSpecializationArgs);
            if (_programs.TryGetValue(key, out SlangProgram? cached))
                return cached;

            if (_cacheDirectory != null)
            {
                long readStart = Stopwatch.GetTimestamp();
                if (TryReadProgram(key, out SlangCachedProgram? payload))
                {
                    SlangProgram restored = SlangProgram.FromCache(logicalName, payload);
                    TrackProgramLocked(restored);
                    _programs[key] = restored;
                    _options.Log?.Invoke(
                        $"slang program '{logicalName}' restored from disk cache in {ElapsedMs(readStart)}ms");
                    return restored;
                }
            }

            long linkStart = Stopwatch.GetTimestamp();
            SlangProgram program = _session.CompileComposed(
                template, companion, valueSpecializationArgs);
            program.Owner = this;
            TrackProgramLocked(program);
            _programs[key] = program;

            if (_cacheDirectory != null)
                WriteProgram(key, program);
            _options.Log?.Invoke(
                $"slang program '{logicalName}' linked in {ElapsedMs(linkStart)}ms");
            return program;
        }
    }

    /// <summary>
    /// The members of a module's named uniform block, read from the module's own
    /// library reflection — no entry points, no link: the material-parameter
    /// probe. Empty when the module declares no such block; throws when the
    /// block's members do not fit the float view.
    /// </summary>
    public IReadOnlyList<ShaderUniformMember> GetModuleUniformMembers(string moduleName, string cbufferName)
        => GetModuleReflection(moduleName).UniformBlocks
            .FirstOrDefault(block => block.Name == cbufferName)?.Members ?? [];

    /// <summary>
    /// The library reflection of a module (see <see cref="ShaderLibraryReflection"/>):
    /// every uniform/parameter block it declares — with user-defined attributes and
    /// float-shaped members — plus every sampled-texture slot, read from the module's
    /// own layout without entry points or a link. Domain-neutral: attribute markers
    /// (e.g. MaterialParams) are filtered by the caller. Cached per module entry;
    /// invalidated together with the module on session rebuilds.
    /// </summary>
    public ShaderLibraryReflection GetModuleReflection(string moduleName)
    {
        // Loads outside the module lock (same pattern as GetComposedProgram).
        GetOrLoadModule(moduleName);
        lock (_lock)
        {
            ModuleEntry entry = _modules[moduleName];
            return entry.LibraryReflection ??= _session.GetModuleReflection(entry.Module);
        }
    }

    private SlangProgram GetProgramLocked(
        string moduleName, ModuleEntry entry, string entriesKey,
        Func<IReadOnlyList<string>, SlangProgram> compile, IReadOnlyList<string> specializationArgs)
    {
        string key = ProgramCacheKey(moduleName, entriesKey, specializationArgs, entry.IrHash);
        if (_programs.TryGetValue(key, out SlangProgram? cached))
            return cached;

        if (_cacheDirectory != null)
        {
            long readStart = Stopwatch.GetTimestamp();
            if (TryReadProgram(key, out SlangCachedProgram? payload))
            {
                SlangProgram restored = SlangProgram.FromCache(moduleName, payload);
                TrackProgramLocked(restored);
                _programs[key] = restored;
                _options.Log?.Invoke(
                    $"slang program '{moduleName}' restored from disk cache in {ElapsedMs(readStart)}ms");
                return restored;
            }
        }

        long linkStart = Stopwatch.GetTimestamp();
        SlangProgram program = compile(specializationArgs);
        program.Owner = this;
        TrackProgramLocked(program);
        _programs[key] = program;

        if (_cacheDirectory != null)
            WriteProgram(key, program);
        _options.Log?.Invoke($"slang program '{moduleName}' linked in {ElapsedMs(linkStart)}ms");
        return program;
    }

    /// <summary>
    /// Drops every module whose dependency graph contains <paramref name="filePath"/> and rebuilds
    /// the compile session. Returns the affected module names (empty when nothing depended on it).
    /// </summary>
    public IReadOnlyList<string> InvalidateModulesContaining(string filePath)
    {
        string normalized = SlangPathUtility.NormalizePath(filePath);
        lock (_lock)
        {
            List<string> affected = _fileToModules.TryGetValue(normalized, out HashSet<string>? modules)
                ? [.. modules.Select(key => _modules.TryGetValue(key, out ModuleEntry? entry) ? entry.LogicalName : key).Distinct()]
                : [];
            if (affected.Count == 0)
                return [];

            RebuildSessionLocked();
            ModulesInvalidated?.Invoke(affected);
            return affected;
        }
    }

    internal void NotifyProgramDisposed(SlangProgram program)
    {
        lock (_lock)
        {
            _livePrograms.Remove(program);
        }
    }

    private void RebuildSessionLocked()
    {
        // Release native programs while their session is still alive, then drop
        // the session (imported slang modules are cached inside it and cannot
        // be invalidated individually).
        foreach (SlangProgram program in _livePrograms)
        {
            program.Linked?.Release();
            program.Linked = null;
        }
        _livePrograms.Clear();
        _programs.Clear();
        _modules.Clear();
        _fileToModules.Clear();
        _rootLoadedPaths.Clear();
        // Only the compile session is rebuilt — the global slang session is
        // process-wide and stays valid (see SlangCompiler).
        _session.Dispose();
        CreateSession();
    }

    private void TrackProgramLocked(SlangProgram program) => _livePrograms.Add(program);

    private ModuleEntry LoadModuleLocked(string key, string pathIdentity, string source)
    {
        if (_cacheDirectory != null)
        {
            long readStart = Stopwatch.GetTimestamp();
            if (TryReadModuleCache(key, pathIdentity, source, out ModuleEntry? cached))
            {
                _options.Log?.Invoke(
                    $"slang module '{key}' restored from IR cache in {ElapsedMs(readStart)}ms");
                return cached!;
            }
        }

        // slang keys root-loaded modules by PATH identity: a second module made
        // from the same file (e.g. two module names over one source) must
        // enter under a distinct one, or slang's dictionary assert fires on the
        // duplicate add.
        string modulePath = pathIdentity;
        if (_rootLoadedPaths.Contains(modulePath))
        {
            string directory = Path.GetDirectoryName(pathIdentity) ?? "";
            string stem = Path.GetFileNameWithoutExtension(pathIdentity);
            string extension = Path.GetExtension(pathIdentity);
            string disambiguator = Convert.ToHexString(XxHash3.Hash(
                System.Text.Encoding.UTF8.GetBytes(key)))[..8];
            modulePath = Path.Combine(directory, $"{stem}_{disambiguator}{extension}");
        }
        _rootLoadedPaths.Add(modulePath);
        long parseStart = Stopwatch.GetTimestamp();
        SlangModuleHandle module = _session.LoadModuleFromSource(key, modulePath, source);
        byte[]? ir = module.Serialize();
        ModuleEntry entry = new()
        {
            // The key (not the path identity) names the module for consumers:
            // InvalidateModulesContaining/GetLoadedModuleNames report logical names.
            LogicalName = key,
            Module = module,
            Dependencies = [.. module.GetDependencyFilePaths().Select(SlangPathUtility.NormalizePath)],
            SerializedIR = ir,
            IrHash = ir != null ? Convert.ToHexString(XxHash3.Hash(ir)) : "",
        };
        string normalizedIdentity = SlangPathUtility.NormalizePath(pathIdentity);
        if (modulePath != pathIdentity && !entry.Dependencies.Contains(normalizedIdentity))
        {
            // The entry's path identity was disambiguated, so record the ORIGINAL
            // path as a dependency too — file invalidation must reach the
            // permutation together with its base module.
            entry.Dependencies.Add(normalizedIdentity);
        }
        if (_cacheDirectory != null)
            WriteModuleCache(key, modulePath, entry, source);
        _options.Log?.Invoke($"slang module '{key}' parsed from source in {ElapsedMs(parseStart)}ms");
        return entry;
    }

    // ── module disk cache ────────────────────────────────────────────────────

    // The cache file identity includes the code target: one machine may switch
    // graphics backends (Vulkan ↔ D3D12) and both targets' IR must coexist.
    private string ModuleCachePath(string moduleKey, string extension) =>
        Path.Combine(_cacheDirectory!, "modules",
            $"{Convert.ToHexString(XxHash3.Hash(System.Text.Encoding.UTF8.GetBytes($"{moduleKey}|{(int)_options.Target}")))}.{extension}");

    private bool TryReadModuleCache(string key, string pathIdentity, string source, out ModuleEntry? entry)
    {
        entry = null;
        string blobPath = ModuleCachePath(key, "slang-module");
        string metaPath = ModuleCachePath(key, "meta");
        if (!File.Exists(blobPath) || !File.Exists(metaPath))
            return false;
        try
        {
            byte[] ir = File.ReadAllBytes(blobPath);
            using FileStream stream = File.OpenRead(metaPath);
            using var reader = new System.IO.BinaryReader(stream);
            if (reader.ReadInt32() != MetaVersion)
                return false;
            if (reader.ReadString() != BuildTag)
                return false;
            // Serialized IR is stamped with the code target: a blob front-end-
            // compiled under one target must not be restored into another's session.
            if (reader.ReadInt32() != (int)_options.Target)
                return false;
            // Own-source staleness: the exact source this entry was built from
            // (the resolved file content) hashes equal without a resolver
            // round-trip.
            if (reader.ReadString() != HashContent(source))
                return false;
            int depCount = reader.ReadInt32();
            List<string> dependencies = new(depCount + 1);
            for (int i = 0; i < depCount; i++)
            {
                string depPath = reader.ReadString();
                string depHash = reader.ReadString();
                // Staleness: every recorded file dependency must still resolve to
                // the recorded content (virtual modules first, then the resolver).
                string? content = ResolveDependency(depPath);
                if (content == null || HashContent(content) != depHash)
                    return false;
                dependencies.Add(depPath);
            }
            // The module's own path is not a recorded dependency (its content is
            // the hashed source above); re-add it so file invalidation still
            // reaches the restored module.
            string normalizedIdentity = SlangPathUtility.NormalizePath(pathIdentity);
            if (!dependencies.Contains(normalizedIdentity))
                dependencies.Add(normalizedIdentity);

            SlangModuleHandle module = _session.LoadModuleFromIRBlob(key, blobPath, ir);
            entry = new ModuleEntry
            {
                LogicalName = key,
                Module = module,
                Dependencies = dependencies,
                SerializedIR = ir,
                IrHash = Convert.ToHexString(XxHash3.Hash(ir)),
                LoadedFromIR = true,
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void WriteModuleCache(string key, string modulePath, ModuleEntry entry, string source)
    {
        if (entry.SerializedIR == null)
            return;
        try
        {
            // File deps exclude the module's own path identity: it is not
            // validated through the resolver (the source hash covers it).
            string ownPath = SlangPathUtility.NormalizePath(modulePath);
            List<string> fileDeps = entry.Dependencies.Where(dep => dep != ownPath).ToList();
            string blobPath = ModuleCachePath(key, "slang-module");
            string metaPath = ModuleCachePath(key, "meta");
            File.WriteAllBytes(blobPath, entry.SerializedIR);
            using FileStream stream = File.Create(metaPath);
            using var writer = new System.IO.BinaryWriter(stream);
            writer.Write(MetaVersion);
            writer.Write(BuildTag);
            writer.Write((int)_options.Target);
            writer.Write(HashContent(source));
            writer.Write(fileDeps.Count);
            foreach (string dep in fileDeps)
            {
                writer.Write(dep);
                writer.Write(HashContent(ResolveDependency(dep) ?? ""));
            }
        }
        catch
        {
            // Cache write failures are non-fatal: compilation already succeeded.
        }
    }

    // ── program disk cache ───────────────────────────────────────────────────

    private string ProgramCachePath(string key) =>
        Path.Combine(_cacheDirectory!, "programs", $"{key}.bin");

    private string ProgramCacheKey(
        string moduleName, string entriesKey, IReadOnlyList<string> specArgs, string irHash)
    {
        using MemoryStream stream = new();
        using var writer = new System.IO.BinaryWriter(stream);
        writer.Write(ProgramCacheKeyVersion);
        writer.Write(BuildTag);
        writer.Write(_options.OptimizationLevel);
        writer.Write((int)_options.Target);
        writer.Write(_options.EffectiveTargetProfile);
        writer.Write(moduleName);
        writer.Write(irHash);
        writer.Write(entriesKey);
        writer.Write(specArgs.Count);
        foreach (string arg in specArgs)
            writer.Write(arg);
        writer.Flush();
        return Convert.ToHexString(XxHash3.Hash(stream.ToArray()));
    }

    // The composed (template × companion) key carries BOTH modules' identities and IR
    // hashes plus the specialization arguments: either side changing must produce a
    // distinct program.
    private string ComposedProgramCacheKey(
        string templateKey, string templateIrHash, string companionKey, string companionIrHash,
        IReadOnlyList<string> valueSpecArgs)
    {
        using MemoryStream stream = new();
        using var writer = new System.IO.BinaryWriter(stream);
        writer.Write(ProgramCacheKeyVersion);
        writer.Write(BuildTag);
        writer.Write(_options.OptimizationLevel);
        writer.Write((int)_options.Target);
        writer.Write(_options.EffectiveTargetProfile);
        writer.Write("composed");
        writer.Write(templateKey);
        writer.Write(templateIrHash);
        writer.Write(companionKey);
        writer.Write(companionIrHash);
        writer.Write("discovered");
        writer.Write(valueSpecArgs.Count);
        foreach (string arg in valueSpecArgs)
            writer.Write(arg);
        writer.Flush();
        return Convert.ToHexString(XxHash3.Hash(stream.ToArray()));
    }

    private bool TryReadProgram(string key, [NotNullWhen(true)] out SlangCachedProgram? payload)
    {
        payload = null;
        string path = ProgramCachePath(key);
        if (!File.Exists(path))
            return false;
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var reader = new System.IO.BinaryReader(stream);
            payload = SlangProgramCacheCodec.Decode(reader);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void WriteProgram(string key, SlangProgram program)
    {
        try
        {
            using FileStream stream = File.Create(ProgramCachePath(key));
            using var writer = new System.IO.BinaryWriter(stream);
            SlangProgramCacheCodec.Encode(writer, new SlangCachedProgram
            {
                EntryCode = program.EntryCode,
                EntryPoints = [.. program.EntryPoints],
                Reflection = program.Reflection,
            });
        }
        catch
        {
            // Non-fatal: the in-memory program is already valid.
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The probed source of a module name (the same probing GetOrLoadModule
    /// uses): the candidate path that hit and its content. The candidate is the
    /// module's path identity — resolver-addressable, unlike the extension-less
    /// module name — so dependency records and cache validation round-trip.
    /// </summary>
    public string? GetModuleSource(string moduleName)
    {
        lock (_lock)
        {
            return _options.Resolver == null ? null : ResolveModuleSource(moduleName)?.Source;
        }
    }

    private (string Candidate, string Source)? ResolveModuleSource(string moduleName)
    {
        string dotted = moduleName.Replace('.', '/');
        string dashed = moduleName.Replace('.', '-');
        string underscored = moduleName.Replace('.', '_');
        foreach (string candidate in new[]
                 {
                     $"{dotted}.slang", $"{dashed}.slang", $"{underscored}.slang", $"{moduleName}.slang",
                 })
        {
            string? content = _options.Resolver!(candidate);
            if (content != null)
                return (candidate, content);
        }
        return null;
    }

    /// <summary>
    /// Content of a dependency path: virtual modules first, then the file
    /// resolver — the same order session resolution uses, so cache validation
    /// sees what the compiler saw.
    /// </summary>
    private string? ResolveDependency(string path)
    {
        if (_virtualSources.TryGetValue(SlangPathUtility.NormalizePath(path), out string? virtualSource))
            return virtualSource;
        return _options.Resolver?.Invoke(path);
    }

    private static string HashContent(string content) =>
        Convert.ToHexString(XxHash3.Hash(System.Text.Encoding.UTF8.GetBytes(content)));

    /// <summary>Whole milliseconds since a <see cref="Stopwatch.GetTimestamp"/> snapshot, without allocating a Stopwatch.</summary>
    private static long ElapsedMs(long startTimestamp) =>
        (long)Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;

    private void IndexDependenciesLocked(string moduleKey, ModuleEntry entry)
    {
        foreach (string dep in entry.Dependencies)
        {
            if (!_fileToModules.TryGetValue(dep, out HashSet<string>? importers))
            {
                importers = [];
                _fileToModules[dep] = importers;
            }
            importers.Add(moduleKey);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (SlangProgram program in _livePrograms)
            {
                program.Linked?.Release();
                program.Linked = null;
            }
            _livePrograms.Clear();
            _programs.Clear();
            _modules.Clear();
            _fileToModules.Clear();
            _session.Dispose();
        }
    }

    private sealed class ModuleEntry
    {
        public string LogicalName { get; init; } = "";
        public required SlangModuleHandle Module { get; init; }
        public required List<string> Dependencies { get; init; }
        public byte[]? SerializedIR { get; init; }
        public string IrHash { get; init; } = "";
        public bool LoadedFromIR { get; init; }

        /// <summary>
        /// Cached module-level reflection (see <see cref="GetModuleReflection"/>); dies with
        /// the entry on session rebuilds — modules are immutable, so it never goes stale
        /// while the entry lives.
        /// </summary>
        public ShaderLibraryReflection? LibraryReflection { get; set; }
    }
}
