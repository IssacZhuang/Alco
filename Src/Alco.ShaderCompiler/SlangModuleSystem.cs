using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Text.RegularExpressions;
using Alco.Graphics;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// SlangModuleSystem (plan §4.2, headless core): owns the slang compile session,
// the module cache, the dependency graph with reverse invalidation, and the two
// disk-cache layers that replace ShaderCache:
//
//   (a) modules/<hash>.slang-module + .meta — serialized module IR. The meta
//       sidecar stamps the slang build tag, the session's code target and
//       every dependency's content hash, because isBinaryModuleUpToDate
//       accepts source-less blobs without validation (the plan's explicit
//       caveat).
//   (b) programs/<hash>.bin — linked programs (per-entry target code +
//       materialized reflection + uniform members), keyed by module IR hash,
//       entry set, specialization, code target and build tag.
//
// Invalidation rebuilds the whole session: slang caches imported modules inside
// the session and modules are immutable, so a changed lib can only be observed
// through a fresh session. Unaffected modules reload from the IR disk cache.
// The Alco.Rendering ShaderSystem wraps this class and turns ModulesInvalidated
// into Shader version bumps; nothing here touches GPU types.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Owns slang modules, their dependency graph and the shader disk caches.</summary>
public sealed partial class SlangModuleSystem : IDisposable
{
    private const int MetaVersion = 2;
    private const int ProgramCacheKeyVersion = 4;

    private readonly SlangCompilerOptions _options;
    private readonly string? _cacheDirectory;
    private readonly Lock _lock = new();

    private SlangCompiler _compiler = null!;
    private SlangCompileSession _session = null!;

    private readonly Dictionary<string, ModuleEntry> _modules = new();
    private readonly Dictionary<string, HashSet<string>> _fileToModules = new(); // file → module names
    private readonly Dictionary<string, SlangProgram> _programs = new();         // cache key → program
    private readonly List<SlangProgram> _livePrograms = [];
    private readonly HashSet<string> _rootLoadedPaths = new(StringComparer.OrdinalIgnoreCase);

    // Virtual module sources, resolved by name ahead of the file resolver:
    // generated wrapper modules and define-mangled permutations (the material
    // compiler's template+surface compositions). Sources survive session
    // rebuilds — they are inputs, like files.
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
        _compiler = SlangCompiler.Create();
        // Virtual modules resolve ahead of the asset resolver so a wrapper's
        // import-by-name reaches generated and define-mangled modules.
        SlangCompilerOptions sessionOptions = _options.Resolver == null
            ? _options
            : new SlangCompilerOptions
            {
                SearchPaths = _options.SearchPaths,
                PreprocessorMacros = _options.PreprocessorMacros,
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
    /// <paramref name="defines"/> selects a preprocessor permutation of the
    /// module — the material-keyword mechanism (user asset keywords,
    /// SHADOW_CUTOUT, REPEATED): each set is a distinct module identity with
    /// its own caches, realized as #define lines prefixed to the resolved
    /// source. Engine-owned variant axes use generic value specializations
    /// instead (see GetProgram's specialization arguments).
    /// </summary>
    public SlangModuleHandle GetOrLoadModule(string moduleName, IReadOnlyList<string>? defines = null)
    {
        lock (_lock)
        {
            string key = ModuleKey(moduleName, defines);
            if (_modules.TryGetValue(key, out ModuleEntry? existing))
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
                _modules[key] = raw;
                IndexDependenciesLocked(key, raw);
                return module;
            }

            // Probe the module's source through the name→file conventions the
            // resolver implements, then load from that source.
            string? source = ResolveModuleSource(moduleName);
            if (source == null)
                throw new ShaderCompilationException($"slang module '{moduleName}' not found through the resolver.");
            if (defines is { Count: > 0 })
            {
                string suffix = string.Concat(defines.Select(define => "_" + define));
                source = string.Concat(defines.Select(define => "#define " + define + " 1\n")) + source;
                // A define permutation reuses the file's source, including its
                // `module X;` declaration — slang keys a session's module table
                // by the DECLARED name, so the permutation must declare a
                // mangled one or the second load trips slang's dictionary assert.
                source = ModuleDeclarationRegex().Replace(
                    source, m => $"{m.Groups[1].Value}module {m.Groups[2].Value}{suffix};");
            }
            ModuleEntry entry = LoadModuleLocked(key, moduleName, source);
            _modules[key] = entry;
            IndexDependenciesLocked(key, entry);
            return entry.Module;
        }
    }

    internal static string ModuleKey(string moduleName, IReadOnlyList<string>? defines)
        => defines is { Count: > 0 } ? $"{moduleName}|{string.Join("|", defines)}" : moduleName;

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

    /// <summary>
    /// Links (or restores) one program of a loaded module. <paramref name="entryPoints"/> order
    /// defines the EntryCode order.
    /// </summary>
    public SlangProgram GetProgram(
        string moduleName, IReadOnlyList<SlangEntryPointRequest> entryPoints, IReadOnlyList<string> specializationArgs,
        IReadOnlyList<string>? defines = null)
    {
        lock (_lock)
        {
            if (!_modules.TryGetValue(ModuleKey(moduleName, defines), out ModuleEntry? entry))
                throw new InvalidOperationException(
                    $"Module '{moduleName}' is not loaded; call GetOrLoadModule first.");

            string entriesKey = string.Join(";", entryPoints.Select(request => $"{request.Name}:{(int)request.Stage}"));
            return GetProgramLocked(ModuleKey(moduleName, defines), entry, entriesKey,
                specArgs => _session.Compile(entry.Module, entryPoints, specArgs), specializationArgs);
        }
    }

    /// <summary>
    /// Links (or restores) the program of every [shader(...)] entry point the module defines,
    /// in definition order — the module-name keyed lookup path.
    /// </summary>
    public SlangProgram GetProgramAllEntries(
        string moduleName, IReadOnlyList<string> specializationArgs, IReadOnlyList<string>? defines = null)
    {
        lock (_lock)
        {
            if (!_modules.TryGetValue(ModuleKey(moduleName, defines), out ModuleEntry? entry))
                throw new InvalidOperationException(
                    $"Module '{moduleName}' is not loaded; call GetOrLoadModule first.");

            return GetProgramLocked(ModuleKey(moduleName, defines), entry, "all",
                specArgs => _session.CompileAllEntryPoints(entry.Module, specArgs), specializationArgs);
        }
    }

    private SlangProgram GetProgramLocked(
        string moduleName, ModuleEntry entry, string entriesKey,
        Func<IReadOnlyList<string>, SlangProgram> compile, IReadOnlyList<string> specializationArgs)
    {
        string key = ProgramCacheKey(moduleName, entriesKey, specializationArgs, entry.IrHash);
        if (_programs.TryGetValue(key, out SlangProgram? cached))
            return cached;

        if (_cacheDirectory != null && TryReadProgram(key, out SlangCachedProgram? payload))
        {
            SlangProgram restored = SlangProgram.FromCache(moduleName, payload);
            TrackProgramLocked(restored);
            _programs[key] = restored;
            return restored;
        }

        SlangProgram program = compile(specializationArgs);
        HarvestUniformMembers(program);
        program.Owner = this;
        TrackProgramLocked(program);
        _programs[key] = program;

        if (_cacheDirectory != null)
            WriteProgram(key, program);
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
        _session.Dispose();
        _compiler.Dispose();
        CreateSession();
    }

    private void TrackProgramLocked(SlangProgram program) => _livePrograms.Add(program);

    private ModuleEntry LoadModuleLocked(string key, string moduleName, string source)
    {
        if (_cacheDirectory != null && TryReadModuleCache(key, out ModuleEntry? cached))
            return cached!;

        // slang keys root-loaded modules by PATH identity: a second module made
        // from the same file (a define permutation registered under a distinct
        // name) must enter under a distinct one, or slang's dictionary assert
        // fires on the duplicate add.
        string loadName = key.Replace('|', '_');
        string pathIdentity = moduleName;
        if (_rootLoadedPaths.Contains(pathIdentity))
        {
            string directory = Path.GetDirectoryName(moduleName) ?? "";
            string stem = Path.GetFileNameWithoutExtension(moduleName);
            string extension = Path.GetExtension(moduleName);
            string disambiguator = Convert.ToHexString(XxHash3.Hash(
                System.Text.Encoding.UTF8.GetBytes(key)))[..8];
            pathIdentity = Path.Combine(directory, $"{stem}_{disambiguator}{extension}");
        }
        _rootLoadedPaths.Add(pathIdentity);
        SlangModuleHandle module = _session.LoadModuleFromSource(loadName, pathIdentity, source);
        byte[]? ir = module.Serialize();
        ModuleEntry entry = new()
        {
            // The key (not the path identity) names the module for consumers:
            // InvalidateModulesContaining/GetLoadedModuleNames report logical names.
            LogicalName = key.Split('|')[0],
            Module = module,
            Dependencies = [.. module.GetDependencyFilePaths().Select(SlangPathUtility.NormalizePath)],
            SerializedIR = ir,
            IrHash = ir != null ? Convert.ToHexString(XxHash3.Hash(ir)) : "",
        };
        if (pathIdentity != moduleName && !entry.Dependencies.Contains(moduleName))
        {
            // The entry's path identity was disambiguated, so record the ORIGINAL
            // path as a dependency too — file invalidation must reach the
            // permutation together with its base module.
            entry.Dependencies.Add(moduleName);
        }
        if (_cacheDirectory != null)
            WriteModuleCache(key, moduleName, entry);
        return entry;
    }

    // ── module disk cache ────────────────────────────────────────────────────

    // The cache file identity includes the code target: one machine may switch
    // graphics backends (Vulkan ↔ D3D12) and both targets' IR must coexist.
    private string ModuleCachePath(string moduleName, string extension) =>
        Path.Combine(_cacheDirectory!, "modules",
            $"{Convert.ToHexString(XxHash3.Hash(System.Text.Encoding.UTF8.GetBytes($"{moduleName}|{(int)_options.Target}")))}.{extension}");

    private bool TryReadModuleCache(string moduleName, out ModuleEntry? entry)
    {
        entry = null;
        string blobPath = ModuleCachePath(moduleName, "slang-module");
        string metaPath = ModuleCachePath(moduleName, "meta");
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
            int depCount = reader.ReadInt32();
            List<string> dependencies = new(depCount);
            for (int i = 0; i < depCount; i++)
            {
                string depPath = reader.ReadString();
                string depHash = reader.ReadString();
                // Staleness: every dependency must still hash to the recorded value.
                string? content = _options.Resolver?.Invoke(depPath);
                if (content == null || HashContent(content) != depHash)
                    return false;
                dependencies.Add(depPath);
            }

            SlangModuleHandle module = _session.LoadModuleFromIRBlob(moduleName.Replace('|', '_'), blobPath, ir);
            entry = new ModuleEntry
            {
                LogicalName = moduleName.Split('|')[0],
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

    private void WriteModuleCache(string moduleName, string path, ModuleEntry entry)
    {
        if (entry.SerializedIR == null || _options.Resolver == null)
            return;
        try
        {
            string blobPath = ModuleCachePath(moduleName, "slang-module");
            string metaPath = ModuleCachePath(moduleName, "meta");
            File.WriteAllBytes(blobPath, entry.SerializedIR);
            using FileStream stream = File.Create(metaPath);
            using var writer = new System.IO.BinaryWriter(stream);
            writer.Write(MetaVersion);
            writer.Write(BuildTag);
            writer.Write((int)_options.Target);
            writer.Write(entry.Dependencies.Count);
            foreach (string dep in entry.Dependencies)
            {
                writer.Write(dep);
                writer.Write(HashContent(_options.Resolver(dep) ?? ""));
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
            Dictionary<string, List<SlangUniformMember>> members = new(program.UniformMembers);
            using FileStream stream = File.Create(ProgramCachePath(key));
            using var writer = new System.IO.BinaryWriter(stream);
            SlangProgramCacheCodec.Encode(writer, new SlangCachedProgram
            {
                EntryCode = program.EntryCode,
                EntryPoints = [.. program.EntryPoints],
                Reflection = program.Reflection,
                UniformMembers = members,
            });
        }
        catch
        {
            // Non-fatal: the in-memory program is already valid.
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void HarvestUniformMembers(SlangProgram program)
    {
        // Best-effort pre-harvest for the disk cache: only material-parameter
        // blocks fit the float-only contract. Engine-managed buffers (uint
        // frame counters, nested camera/data structs) are skipped here; a
        // material block with non-float members still throws when the caller
        // requests it by name through the lazy path.
        Dictionary<string, List<SlangUniformMember>> members = new();
        foreach (BindGroupLayout group in program.Reflection.BindGroups)
        {
            foreach (BindGroupEntryInfo binding in group.Bindings)
            {
                if (binding.Entry.Type == BindingType.UniformBuffer)
                {
                    try
                    {
                        members[binding.Entry.Name] = program.GetUniformMembers(binding.Entry.Name);
                    }
                    catch (NotSupportedException)
                    {
                        // Not a material parameter block — leave uncached.
                    }
                }
            }
        }
        program.UniformMembers = members;
    }

    /// <summary>Resolves a module by slang's name→file conventions ('a.b' → 'a/b.slang', 'a-b.slang', …).</summary>
    /// <summary>
    /// The resolved source of a module name (the same probing GetOrLoadModule
    /// uses), or null — e.g. for permutation enumeration on the shader side.
    /// </summary>
    public string? GetModuleSource(string moduleName)
    {
        lock (_lock)
        {
            return _options.Resolver == null ? null : ResolveModuleSource(moduleName);
        }
    }

    private string? ResolveModuleSource(string moduleName)
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
                return content;
        }
        return null;
    }

    private static string HashContent(string content) =>
        Convert.ToHexString(XxHash3.Hash(System.Text.Encoding.UTF8.GetBytes(content)));

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
            _compiler.Dispose();
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
    }

    [GeneratedRegex(@"(^|\n)[ \t]*module[ \t]+([A-Za-z_][A-Za-z0-9_.]*)[ \t]*;")]
    private static partial Regex ModuleDeclarationRegex();
}
