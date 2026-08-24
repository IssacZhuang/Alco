using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// ShaderSystem (plan §4.2, runtime service): the module-name keyed shader
// factory on top of SlangModuleSystem. Callers ask for
// GetShader(moduleName, specialization…) instead of Load<Shader>(path); the
// returned Shader is the unified (module, entries, specialization) object.
//
// Compatibility defines and specialization arguments are both part of the
// module/program cache identity.
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
    private readonly Lock _lock = new();
    private readonly Dictionary<(string Module, string Specialization), Shader> _shaders = new();
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

    /// <summary>Gets (or creates) the shader of one module with its default specialization.</summary>
    public Shader GetShader(string moduleName)
        => GetShader(moduleName, ReadOnlySpan<string>.Empty);

    /// <summary>
    /// Gets (or creates) the shader of one module with the given specialization arguments
    /// (generic type/value instantiations, plan D3).
    /// </summary>
    public Shader GetShader(string moduleName, params ReadOnlySpan<string> specializationArgs)
    {
        string specKey = string.Join("|", specializationArgs.ToArray());
        string[] specialization = specializationArgs.ToArray();
        lock (_lock)
        {
            if (_shaders.TryGetValue((moduleName, specKey), out Shader? cached))
                return cached;

            // Loads through the resolver's name→source conventions; entry points
            // are the module's own [shader(...)] definitions.
            _modules.GetOrLoadModule(moduleName);

            Shader shader = _renderingSystem.CreateShader(
                specializationArgs.Length == 0 ? moduleName : $"{moduleName}[{specKey}]",
                defines => CompileModules(moduleName, specialization, defines),
                permutationSource: _modules.GetModuleSource(moduleName));
            _shaders[(moduleName, specKey)] = shader;
            return shader;
        }
    }

    /// <summary>
    /// Gets (or creates) the shader of a module registered from explicit source instead of
    /// the file resolver — embedded resources (ImGui) and generated wrappers (the material
    /// compiler's template+surface compositions) enter the module system this way. The
    /// module keeps its source identity: dependency tracking and invalidation treat
    /// <paramref name="path"/> like any other module file. Defines permutations are not
    /// supported for source-registered modules (their permutations are distinct module
    /// identities owned by the source generator).
    /// </summary>
    /// <param name="moduleName">The module identity (its import name).</param>
    /// <param name="path">The virtual path carrying the module's identity in dependency graphs and caches.</param>
    /// <param name="source">The module source.</param>
    /// <param name="customVertexLayouts">Optional vertex layout override (e.g. ImGui's packed vertex color).</param>
    public Shader GetShaderFromModule(string moduleName, string path, string source,
        IReadOnlyList<VertexInputLayout>? customVertexLayouts = null)
    {
        lock (_lock)
        {
            if (_shaders.TryGetValue((moduleName, ""), out Shader? cached))
                return cached;

            _modules.GetOrLoadModule(moduleName, path, source);

            Shader shader = _renderingSystem.CreateShader(
                moduleName, defines => CompileModules(moduleName, [], defines), customVertexLayouts,
                permutationSource: source);
            _shaders[(moduleName, "")] = shader;
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

    private ShaderModulesInfo CompileModules(string moduleName, string[] specializationArgs, string[] defines)
    {
        // Re-resolve the module: after an invalidation the module cache is empty
        // and the shader's provider runs again on first use. Defines select a
        // preprocessor permutation (a distinct module identity in the cache).
        _modules.GetOrLoadModule(moduleName, defines);

        // Programs stay pinned: ShaderModule structs reference the SPIR-V arrays.
        SlangProgram program = _modules.GetProgramAllEntries(moduleName, specializationArgs, defines);
        lock (_lock)
        {
            _pinnedPrograms.Add(program);
        }

        return BuildModulesInfo(_renderingSystem, _modules.Target, moduleName, specializationArgs, defines, program);
    }

    /// <summary>
    /// Builds the engine shader-modules view of one linked slang program: bind-group
    /// validation against the device limit, per-stage <see cref="ShaderModule"/>s and
    /// the VS+FS / CS-only variant split. Shared with the material composer, whose
    /// programs come from template×surface composition instead of a single module.
    /// </summary>
    internal static ShaderModulesInfo BuildModulesInfo(
        RenderingSystem renderingSystem, SlangCodeTarget target,
        string name, string[] specializationArgs, string[] defines, SlangProgram program)
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
            return ShaderModulesInfo.CreateGraphics(name, specializationArgs, vs, fs, program.Reflection);
        }
        if (compute is { } cs)
        {
            return ShaderModulesInfo.CreateCompute(name, specializationArgs, cs, program.Reflection);
        }
        throw new InvalidOperationException(
            $"slang module '{name}' defines no usable vertex/fragment/compute entry point combination.");
    }

    private void OnModulesInvalidated(IReadOnlyList<string> affectedModules)
    {
        List<Shader> affectedShaders;
        lock (_lock)
        {
            // Stale programs die with the session rebuild; pins are refreshed lazily.
            _pinnedPrograms.Clear();
            affectedShaders =
            [
                .. _shaders.Where(pair => affectedModules.Contains(pair.Key.Module))
                           .Select(pair => pair.Value),
            ];
        }
        foreach (Shader shader in affectedShaders)
        {
            shader.UnsafeModuleReload();
            ShaderInvalidated?.Invoke(shader);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _pinnedPrograms.Clear();
            foreach (Shader shader in _shaders.Values)
            {
                shader.Dispose();
            }
            _shaders.Clear();
        }
        _modules.Dispose();
    }
}
