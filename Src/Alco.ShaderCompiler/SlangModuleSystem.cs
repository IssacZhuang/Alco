using System.IO.Hashing;
using Alco.Graphics;

namespace Alco.ShaderCompiler;

// ─────────────────────────────────────────────────────────────────────────────
// SlangModuleSystem (plan §4.2, headless core): owns the slang compile session,
// the module cache, the dependency graph with reverse invalidation, and the two
// disk-cache layers that replace ShaderCache:
//
//   (a) modules/<hash>.slang-module + .meta — serialized module IR. The meta
//       sidecar stamps the slang build tag and every dependency's content hash,
//       because isBinaryModuleUpToDate accepts source-less blobs without
//       validation (the plan's explicit caveat).
//   (b) programs/<hash>.bin — linked programs (per-entry SPIR-V + materialized
//       reflection + uniform members), keyed by module IR hash, entry set,
//       specialization and build tag.
//
// Invalidation rebuilds the whole session: slang caches imported modules inside
// the session and modules are immutable, so a changed lib can only be observed
// through a fresh session. Unaffected modules reload from the IR disk cache.
// The Alco.Rendering ShaderSystem wraps this class and turns ModulesInvalidated
// into Shader version bumps; nothing here touches GPU types.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Owns slang modules, their dependency graph and the shader disk caches.</summary>
public sealed class SlangModuleSystem : IDisposable
{
    private const int MetaVersion = 1;

    private readonly SlangCompilerOptions _options;
    private readonly string? _cacheDirectory;
    private readonly Lock _lock = new();

    private SlangCompiler _compiler = null!;
    private SlangCompileSession _session = null!;

    private readonly Dictionary<string, ModuleEntry> _modules = new();
    private readonly Dictionary<string, HashSet<string>> _fileToModules = new(); // file → module names
    private readonly Dictionary<string, SlangProgram> _programs = new();         // cache key → program
    private readonly List<SlangProgram> _livePrograms = [];

    /// <summary>The pinned slang release's build tag — part of every cache key.</summary>
    public string BuildTag { get; }

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
        _session = _compiler.CreateSession(_options);
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

    /// <summary>Loads a module by name through the session's search paths / resolver.</summary>
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
                    Module = module,
                    Dependencies = [.. module.GetDependencyFilePaths().Select(SlangPathUtility.NormalizePath)],
                };
                _modules[moduleName] = raw;
                IndexDependenciesLocked(moduleName, raw);
                return module;
            }

            // Probe the module's source through the name→file conventions the
            // resolver implements, then load from that source.
            string? source = ResolveModuleSource(moduleName);
            if (source == null)
                throw new ShaderCompilationException($"slang module '{moduleName}' not found through the resolver.");
            ModuleEntry entry = LoadModuleLocked(moduleName, moduleName, source);
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
            return [.. _modules.Keys];
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
    public SlangProgram GetProgramAllEntries(string moduleName, IReadOnlyList<string> specializationArgs)
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
                ? [.. modules]
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
        _session.Dispose();
        _compiler.Dispose();
        CreateSession();
    }

    private void TrackProgramLocked(SlangProgram program) => _livePrograms.Add(program);

    private ModuleEntry LoadModuleLocked(string moduleName, string path, string source)
    {
        if (_cacheDirectory != null && TryReadModuleCache(moduleName, out ModuleEntry? cached))
            return cached!;

        SlangModuleHandle module = _session.LoadModuleFromSource(moduleName, path, source);
        byte[]? ir = module.Serialize();
        ModuleEntry entry = new()
        {
            Module = module,
            Dependencies = [.. module.GetDependencyFilePaths().Select(SlangPathUtility.NormalizePath)],
            SerializedIR = ir,
            IrHash = ir != null ? Convert.ToHexString(XxHash3.Hash(ir)) : "",
        };
        if (_cacheDirectory != null)
            WriteModuleCache(moduleName, path, entry);
        return entry;
    }

    // ── module disk cache ────────────────────────────────────────────────────

    private string ModuleCachePath(string moduleName, string extension) =>
        Path.Combine(_cacheDirectory!, "modules", $"{Convert.ToHexString(XxHash3.Hash(System.Text.Encoding.UTF8.GetBytes(moduleName)))}.{extension}");

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

            SlangModuleHandle module = _session.LoadModuleFromIRBlob(moduleName, blobPath, ir);
            entry = new ModuleEntry
            {
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
        writer.Write(BuildTag);
        writer.Write(moduleName);
        writer.Write(irHash);
        writer.Write(entriesKey);
        writer.Write(specArgs.Count);
        foreach (string arg in specArgs)
            writer.Write(arg);
        writer.Flush();
        return Convert.ToHexString(XxHash3.Hash(stream.ToArray()));
    }

    private bool TryReadProgram(string key, out SlangCachedProgram? payload)
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
        Dictionary<string, List<SlangUniformMember>> members = new();
        foreach (BindGroupLayout group in program.Reflection.BindGroups)
        {
            foreach (BindGroupEntryInfo binding in group.Bindings)
            {
                if (binding.Entry.Type == BindingType.UniformBuffer)
                {
                    members[binding.Entry.Name] = program.GetUniformMembers(binding.Entry.Name);
                }
            }
        }
        program.UniformMembers = members;
    }

    /// <summary>Resolves a module by slang's name→file conventions ('a.b' → 'a/b.slang', 'a-b.slang', …).</summary>
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
        public required SlangModuleHandle Module { get; init; }
        public required List<string> Dependencies { get; init; }
        public byte[]? SerializedIR { get; init; }
        public string IrHash { get; init; } = "";
        public bool LoadedFromIR { get; init; }
    }
}
