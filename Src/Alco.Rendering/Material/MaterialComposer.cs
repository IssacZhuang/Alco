using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// MaterialComposer: the pipeline-agnostic material-composition primitive. A
// "pass template" slang module owns generic [shader] entry points over a
// surface contract (interface); a "surface" module exports the concrete surface
// type. Composition is slang's own component system (composite + link-time
// specialization) — no generated wrapper modules, no preprocessor stitching:
//
//   shader = composer.ComposeGraphics("gbuffer", "my_surface");
//
// Every generic entry point takes the surface type as its first specialization
// argument; value specialization arguments (e.g. the shadow template's
// <let AlphaTest : bool>) feed the entries' value parameters in entry order.
// Composed shaders are cached per (template, surface, type, args, kind), ride
// the module system's disk caches, and hot-reload with it: when either module
// is invalidated the shader's caches are cleared and ShaderInvalidated fires so
// consumers can re-record static render bundles.
//
// The composer also owns the material-parameter convention: a surface may
// declare a cbuffer whose members mix scalar/vector float types; the engine
// reads the layout from slang's module-level reflection (GetParamsLayout — no
// probe compile) and packs a uniform buffer from named values
// (PackParamsBuffer).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Composes pass-template and surface slang modules into cached, hot-reloadable shaders.</summary>
public sealed class MaterialComposer : IDisposable
{
    /// <summary>The surface type name every surface module exports by convention.</summary>
    public const string DefaultSurfaceTypeName = "Surface";

    /// <summary>The conventional name of a surface's material-parameter block.</summary>
    public const string DefaultParamsBlockName = "_materialParams";

    private readonly record struct CompositionKey(
        string TemplateModule,
        string SurfaceModule,
        string SurfaceType,
        string Specialization,
        string Defines,
        bool Compute);

    private readonly RenderingSystem _rendering;
    private readonly ShaderSystem _shaderSystem;
    private readonly Lock _lock = new();
    private readonly Dictionary<CompositionKey, Shader> _shaders = new();
    private readonly Dictionary<(string Module, string Block), List<SlangUniformMember>> _paramLayouts = new();
    private readonly List<SlangProgram> _pinnedPrograms = [];
    private bool _disposed;

    /// <summary>Raised for each composed shader whose template or surface module was invalidated.</summary>
    public event Action<Shader>? ShaderInvalidated;

    /// <summary>
    /// Create the composer over a shader system's module system.
    /// </summary>
    /// <param name="rendering">The rendering system (shader factory, buffer creation).</param>
    /// <param name="shaderSystem">
    /// The shader system owning the slang module system; null uses the rendering system's
    /// shared one (the production path — tests may hand in an isolated instance).
    /// </param>
    public MaterialComposer(RenderingSystem rendering, ShaderSystem? shaderSystem = null)
    {
        _rendering = rendering;
        _shaderSystem = shaderSystem ?? rendering.ShaderSystem;
        _shaderSystem.Modules.ModulesInvalidated += OnModulesInvalidated;
    }

    /// <summary>
    /// The composed graphics (vertex+fragment) shader of one (template, surface) pair;
    /// created on first request, then cached. The composer owns the returned shader.
    /// </summary>
    /// <param name="templateModule">The pass-template module name (owns the generic entry points).</param>
    /// <param name="surfaceModule">The surface module name (exports the surface type).</param>
    /// <param name="valueSpecArgs">Value specialization arguments in entry order (e.g. ["true"] for the shadow template's AlphaTest).</param>
    /// <param name="surfaceType">The companion type name; <see cref="DefaultSurfaceTypeName"/> by convention.</param>
    /// <param name="name">Debug name; defaults to "template+surface[args]".</param>
    /// <param name="defines">
    /// Composition-static preprocessor defines (a material asset's surface feature
    /// toggles): baked into the composition identity and applied to every permutation,
    /// unlike the runtime defines a material may still select per pipeline.
    /// </param>
    public Shader ComposeGraphics(
        string templateModule, string surfaceModule,
        IReadOnlyList<string>? valueSpecArgs = null,
        string surfaceType = DefaultSurfaceTypeName, string? name = null,
        IReadOnlyList<string>? defines = null)
        => Compose(templateModule, surfaceModule, valueSpecArgs, surfaceType, name, compute: false, defines);

    /// <summary>
    /// The composed compute shader of one (template, surface) pair — e.g. the voxel-GI
    /// feed whose template owns a single surface-generic [shader("compute")] entry.
    /// </summary>
    /// <inheritdoc cref="ComposeGraphics"/>
    public Shader ComposeCompute(
        string templateModule, string surfaceModule,
        IReadOnlyList<string>? valueSpecArgs = null,
        string surfaceType = DefaultSurfaceTypeName, string? name = null,
        IReadOnlyList<string>? defines = null)
        => Compose(templateModule, surfaceModule, valueSpecArgs, surfaceType, name, compute: true, defines);

    /// <summary>
    /// The members of a surface module's parameter block, from slang's module-level
    /// reflection — no entry points, no link. Cached per (module, block, defines);
    /// empty means the module declares no such block.
    /// </summary>
    public IReadOnlyList<SlangUniformMember> GetParamsLayout(
        string surfaceModule, string blockName = DefaultParamsBlockName,
        IReadOnlyList<string>? defines = null)
    {
        string definesKey = defines == null ? "" : string.Join("|", defines);
        lock (_lock)
        {
            if (_paramLayouts.TryGetValue((surfaceModule, blockName + definesKey), out List<SlangUniformMember>? cached))
            {
                return cached;
            }
            List<SlangUniformMember> members =
                _shaderSystem.Modules.GetModuleUniformMembers(surfaceModule, blockName, defines);
            _paramLayouts.Add((surfaceModule, blockName + definesKey), members);
            return members;
        }
    }

    /// <summary>
    /// Packs a uniform buffer from a parameter-block layout and named values: members
    /// the value table leaves out read zero; an unknown name is a typo and fails
    /// listing the valid members. The buffer is laid out at the offsets slang
    /// reflected (scalars and vectors may mix), 16-byte aligned.
    /// </summary>
    /// <param name="layout">The block members (<see cref="GetParamsLayout"/>).</param>
    /// <param name="values">The values by member name.</param>
    /// <param name="name">The owner name (error context and buffer label).</param>
    public GraphicsBuffer PackParamsBuffer(
        IReadOnlyList<SlangUniformMember> layout,
        IReadOnlyDictionary<string, float[]> values,
        string name)
    {
        foreach (string key in values.Keys)
        {
            if (layout.All(member => member.Name != key))
            {
                throw new InvalidDataException(
                    $"Parameter '{key}' of '{name}' matches no member of the surface's parameter block; expected one of: {string.Join(", ", layout.Select(member => member.Name))}.");
            }
        }

        uint sizeBytes = 0;
        foreach (SlangUniformMember member in layout)
        {
            sizeBytes = Math.Max(sizeBytes, member.OffsetBytes + member.SizeBytes);
        }
        sizeBytes = (sizeBytes + 15u) & ~15u;

        float[] data = new float[sizeBytes / sizeof(float)];
        foreach (SlangUniformMember member in layout)
        {
            if (!values.TryGetValue(member.Name, out float[]? components))
            {
                continue;
            }
            if (components.Length > member.FloatComponentCount)
            {
                throw new InvalidDataException(
                    $"Parameter '{member.Name}' of '{name}' has {components.Length} components, but the surface member takes {member.FloatComponentCount}.");
            }
            for (int i = 0; i < components.Length; i++)
            {
                data[member.OffsetBytes / sizeof(float) + i] = components[i];
            }
        }

        GraphicsArrayBuffer<float> buffer =
            _rendering.CreateGraphicsArrayBuffer<float>(data.Length, $"{name}_params");
        buffer.UpdateBuffer(data.AsSpan());
        return buffer;
    }

    /// <summary>
    /// The texture slot names of one bind group (a shader's material-frequency set):
    /// what the binding side fills from the material's texture table, with fallbacks
    /// for slots still streaming.
    /// </summary>
    public static IReadOnlyList<string> EnumerateTextureSlots(ShaderReflectionInfo reflection, int groupIndex)
    {
        if (groupIndex >= reflection.BindGroups.Count)
        {
            return [];
        }
        return
        [
            .. reflection.BindGroups[groupIndex].Bindings
                .Where(binding => binding.Entry.Type == BindingType.Texture)
                .Select(binding => binding.Entry.Name),
        ];
    }

    private Shader Compose(
        string templateModule, string surfaceModule,
        IReadOnlyList<string>? valueSpecArgs, string surfaceType, string? name, bool compute,
        IReadOnlyList<string>? defines)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string[] specArgs = valueSpecArgs == null ? [] : [.. valueSpecArgs];
        string[] staticDefines = defines == null ? [] : [.. defines];
        string specKey = string.Join("|", specArgs);
        CompositionKey key = new(templateModule, surfaceModule, surfaceType, specKey,
            string.Join("|", staticDefines), compute);
        lock (_lock)
        {
            if (_shaders.TryGetValue(key, out Shader? cached))
            {
                return cached;
            }

            string shaderName = name
                ?? (specArgs.Length == 0
                    ? $"{templateModule}+{surfaceModule}"
                    : $"{templateModule}+{surfaceModule}[{specKey}]");
            Shader shader = _rendering.CreateShader(
                shaderName,
                runtimeDefines => CompilePermutation(key, specArgs, staticDefines, runtimeDefines, shaderName));
            _shaders.Add(key, shader);
            return shader;
        }
    }

    private ShaderModulesInfo CompilePermutation(
        CompositionKey key, string[] specArgs, string[] staticDefines, string[] runtimeDefines, string shaderName)
    {
        SlangModuleSystem modules = _shaderSystem.Modules;
        // Composition-static defines (the material asset's toggles) always apply;
        // runtime defines a material selects per pipeline append to them.
        string[] defines = staticDefines.Length == 0 && runtimeDefines.Length == 0
            ? []
            : [.. staticDefines, .. runtimeDefines];
        SlangProgram program = modules.GetComposedProgram(
            key.TemplateModule, key.SurfaceModule, key.SurfaceType, specArgs, defines);

        // Programs stay pinned: ShaderModule structs reference the code arrays.
        lock (_lock)
        {
            _pinnedPrograms.Add(program);
        }

        ShaderModulesInfo info = ShaderSystem.BuildModulesInfo(
            _rendering, modules.Target, shaderName, specArgs, defines, program);
        if (key.Compute ? !info.IsComputeShader : !info.IsGraphicsShader)
        {
            throw new InvalidOperationException(
                $"Composed shader '{shaderName}' has the wrong stage mix for {(key.Compute ? "compute" : "graphics")}: " +
                $"the template module must own {(key.Compute ? "a single [shader(\"compute\")] entry" : "[shader(\"vertex\")] and [shader(\"fragment\")] entries")}.");
        }
        return info;
    }

    private void OnModulesInvalidated(IReadOnlyList<string> affectedModules)
    {
        List<Shader> affectedShaders;
        lock (_lock)
        {
            // Stale programs die with the session rebuild; pins are refreshed lazily.
            _pinnedPrograms.Clear();
            // A changed surface module may have changed its parameter block layout.
            foreach ((string module, string block) in _paramLayouts.Keys.ToArray())
            {
                if (affectedModules.Contains(module))
                {
                    _paramLayouts.Remove((module, block));
                }
            }
            affectedShaders =
            [
                .. _shaders.Where(pair =>
                        affectedModules.Contains(pair.Key.TemplateModule) ||
                        affectedModules.Contains(pair.Key.SurfaceModule))
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
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _shaderSystem.Modules.ModulesInvalidated -= OnModulesInvalidated;
        lock (_lock)
        {
            foreach (Shader shader in _shaders.Values)
            {
                shader.Dispose();
            }
            _shaders.Clear();
            _pinnedPrograms.Clear();
            _paramLayouts.Clear();
        }
    }
}
