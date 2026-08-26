using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// ShaderSystem (plan §4.2, runtime service): the module-name keyed shader
// factory on top of SlangModuleSystem. Callers ask for
// GetShader(moduleName, specialization…) instead of Load<Shader>(path); the
// returned Shader is the unified (module, entries, specialization) object.
//
// Specialization arguments are part of the program cache identity.
//
// Hot reload: SlangModuleSystem.ModulesInvalidated → every Shader of an
// affected module gets UnsafeModuleReload (version bump, cache clear) and
// ShaderInvalidated fires so consumers can re-record static render bundles.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Owns module-backed shaders: creation, caching and hot-reload invalidation.</summary>
public sealed class ShaderSystem : IDisposable
{
    private readonly RenderingSystem _renderingSystem;
    private readonly SlangModuleSystem _modules;
    // Interned handles: lock-free reads (ConcurrentDictionary), the create lock
    // keeps one creation per key — the same pattern Shader.GetShaderModules
    // uses. The module system serializes all slang/disk work itself.
    private readonly Lock _lockCreate = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Shader> _shaders = new(StringComparer.Ordinal);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ShaderLibrary> _libraries = new(StringComparer.Ordinal);
    private readonly Lock _lockPins = new();
    private readonly List<SlangProgram> _pinnedPrograms = [];

    /// <summary>Raised for each shader whose module was invalidated (after its caches were cleared).</summary>
    public event Action<Shader>? ShaderInvalidated;

    public ShaderSystem(RenderingSystem renderingSystem, SlangCompilerOptions options, string? cacheDirectory = null)
    {
        _renderingSystem = renderingSystem;
        _modules = new SlangModuleSystem(options, cacheDirectory);
        _modules.ModulesInvalidated += OnModulesInvalidated;
    }

    /// <summary>The headless module system (module cache, dependency graph, disk caches).</summary>
    public SlangModuleSystem Modules => _modules;

    /// <summary>
    /// Gets (or creates) the interned <see cref="ShaderLibrary"/> reference of one module.
    /// Creation acquires the resource: the module is loaded (parsed or restored from
    /// the IR cache) and its reflection materialized, so the returned reference is
    /// usable immediately — a name that resolves to nothing or a module that fails
    /// to parse throws here, not at first use. Library references are the typed
    /// identity the material system composes with; their held reflection is
    /// refreshed in place across hot reloads. Resolver-backed modules only —
    /// modules registered from explicit source (<see cref="GetShaderFromModule"/>)
    /// are not addressable this way.
    /// </summary>
    /// <param name="moduleName">The module name (e.g. <c>PbrStandard</c>).</param>
    /// <exception cref="InvalidDataException">The name resolves to no module source.</exception>
    /// <exception cref="ShaderCompilationException">The module source failed to parse.</exception>
    public ShaderLibrary GetLibrary(string moduleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleName);
        if (_libraries.TryGetValue(moduleName, out ShaderLibrary? cached))
        {
            return cached;
        }
        using (_lockCreate.EnterScope())
        {
            if (_libraries.TryGetValue(moduleName, out cached))
            {
                return cached;
            }
            if (_modules.GetModuleSource(moduleName) == null)
            {
                throw new InvalidDataException(
                    $"Shader library '{moduleName}' resolves to no module source; check the module name " +
                    "(it must match the source file's module declaration, e.g. 'PbrStandard').");
            }
            _modules.GetOrLoadModule(moduleName);
            ShaderLibrary library = new(moduleName, _modules.GetModuleReflection(moduleName));
            _libraries[moduleName] = library;
            return library;
        }
    }

    /// <summary>
    /// Gets (or creates) the shader handle of one module: the module's entry
    /// points are its own [shader(...)] definitions, and generic value variant
    /// axes are requested through the specialization arguments of the Shader's
    /// accessor methods (where the retired defines used to be). Handles are
    /// interned per module name.
    /// </summary>
    public Shader GetShader(string moduleName)
    {
        if (_shaders.TryGetValue(moduleName, out Shader? cached))
            return cached;

        using (_lockCreate.EnterScope())
        {
            if (_shaders.TryGetValue(moduleName, out cached))
                return cached;

            // Loads through the resolver's name→source conventions (validates
            // the module resolves; generic entry points link lazily per
            // specialization, not here).
            _modules.GetOrLoadModule(moduleName);

            Shader shader = _renderingSystem.CreateShader(
                moduleName, specialization => CompileModules(moduleName, specialization));
            _shaders[moduleName] = shader;
            return shader;
        }
    }

    /// <summary>
    /// Gets (or creates) the shader of a module registered from explicit source instead of
    /// the file resolver — embedded resources (ImGui) and generated wrappers (the material
    /// compiler's template+surface compositions) enter the module system this way. The
    /// module keeps its source identity: dependency tracking and invalidation treat
    /// <paramref name="path"/> like any other module file.
    /// </summary>
    /// <param name="moduleName">The module identity (its import name).</param>
    /// <param name="path">The virtual path carrying the module's identity in dependency graphs and caches.</param>
    /// <param name="source">The module source.</param>
    /// <param name="customVertexLayouts">Optional vertex layout override (e.g. ImGui's packed vertex color).</param>
    public Shader GetShaderFromModule(string moduleName, string path, string source,
        IReadOnlyList<VertexInputLayout>? customVertexLayouts = null)
    {
        if (_shaders.TryGetValue(moduleName, out Shader? cached))
            return cached;

        using (_lockCreate.EnterScope())
        {
            if (_shaders.TryGetValue(moduleName, out cached))
                return cached;

            _modules.GetOrLoadModule(moduleName, path, source);

            Shader shader = _renderingSystem.CreateShader(
                moduleName, specialization => CompileModules(moduleName, specialization), customVertexLayouts);
            _shaders[moduleName] = shader;
            return shader;
        }
    }

    /// <summary>
    /// Forwards a file change (watcher path, in the dependency graph's path space) to module
    /// invalidation. Returns the affected module names; every shader of an affected module was
    /// reloaded unsafely and reported through <see cref="ShaderInvalidated"/>.
    /// </summary>
    public IReadOnlyList<string> InvalidateModulesContaining(string filePath)
        => _modules.InvalidateModulesContaining(filePath);

    private ShaderModulesInfo CompileModules(string moduleName, string[] specializationArgs)
    {
        // Re-resolve the module: after an invalidation the module cache is empty
        // and the shader's provider runs again on first use.
        _modules.GetOrLoadModule(moduleName);

        // Programs stay pinned: ShaderModule structs reference the SPIR-V arrays.
        SlangProgram program = _modules.GetProgramAllEntries(moduleName, specializationArgs);
        lock (_lockPins)
        {
            _pinnedPrograms.Add(program);
        }

        return BuildModulesInfo(_renderingSystem, _modules.Target, moduleName, program);
    }

    /// <summary>
    /// Builds the engine shader-modules view of one linked slang program: bind-group
    /// validation against the device limit, per-stage <see cref="ShaderModule"/>s and
    /// the VS+FS / CS-only variant split. Shared with the material composer, whose
    /// programs come from template×surface composition instead of a single module.
    /// </summary>
    internal static ShaderModulesInfo BuildModulesInfo(
        RenderingSystem renderingSystem, SlangCodeTarget target,
        string name, SlangProgram program)
    {
        // Device-limit check (set contiguity is already enforced by the reflection reader).
        ShaderReflectionUtility.ValidateBindGroupLayouts(
            program.Reflection, renderingSystem.GraphicsDevice.MaxBindGroups, name);

        ShaderModule? vertex = null, fragment = null, compute = null;
        for (int i = 0; i < program.EntryPoints.Count; i++)
        {
            (string entryName, int stage) = program.EntryPoints[i];
            ShaderStage engineStage = SlangCompileSession.SlangStageToEngine(stage);
            ShaderModule module = new(
                engineStage,
                target.Language(),
                program.EntryCode[i],
                // slang names every SPIR-V entry point "main" regardless of the
                // source function; DXIL containers and MSL libraries keep the
                // declared names (same rule the beachhead relies on).
                target.EntryPointName(entryName))
            {
                // DXIL/MSL passthrough cannot reflect [numthreads]; carry it for compute.
                WorkgroupSize = engineStage == ShaderStage.Compute
                    ? (program.Reflection.Size.X, program.Reflection.Size.Y, program.Reflection.Size.Z)
                    : (1u, 1u, 1u),
            };
            switch (module.Stage)
            {
                case ShaderStage.Vertex:
                    vertex = module;
                    break;
                case ShaderStage.Fragment:
                    fragment = module;
                    break;
                case ShaderStage.Compute:
                    compute = module;
                    break;
                default:
                    throw new NotSupportedException($"Stage {stage} of entry point '{entryName}' is not supported.");
            }
        }

        if (vertex is { } vs && fragment is { } fs)
        {
            return ShaderModulesInfo.CreateGraphics(name, vs, fs, program.Reflection);
        }
        if (compute is { } cs)
        {
            return ShaderModulesInfo.CreateCompute(name, cs, program.Reflection);
        }
        throw new InvalidOperationException(
            $"slang module '{name}' defines no usable vertex/fragment/compute entry point combination.");
    }

    private void OnModulesInvalidated(IReadOnlyList<string> affectedModules)
    {
        lock (_lockPins)
        {
            // Stale programs die with the session rebuild; pins are refreshed lazily.
            _pinnedPrograms.Clear();
        }
        // Refresh affected libraries' held reflections in place (identity is the
        // point — holders keep their references); a broken edit keeps the
        // last-known-good snapshot until the next invalidation.
        foreach (KeyValuePair<string, ShaderLibrary> pair in _libraries)
        {
            if (!affectedModules.Contains(pair.Key))
                continue;
            try
            {
                _modules.GetOrLoadModule(pair.Key);
                pair.Value.Reflection = _modules.GetModuleReflection(pair.Key);
            }
            catch (Exception ex) when (ex is ShaderCompilationException or InvalidDataException)
            {
                Log.Warning(
                    $"shader library '{pair.Key}' keeps its last-known-good reflection " +
                    $"after a failed hot-reload refresh: {ex.Message}");
            }
        }
        List<Shader> affectedShaders = [.. _shaders.Where(pair => affectedModules.Contains(pair.Key))
                                      .Select(pair => pair.Value)];
        foreach (Shader shader in affectedShaders)
        {
            shader.UnsafeModuleReload();
            ShaderInvalidated?.Invoke(shader);
        }
    }

    public void Dispose()
    {
        lock (_lockPins)
        {
            _pinnedPrograms.Clear();
        }
        foreach (Shader shader in _shaders.Values)
        {
            shader.Dispose();
        }
        _shaders.Clear();
        _modules.Dispose();
    }
}
