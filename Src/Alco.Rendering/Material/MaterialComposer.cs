using System.Numerics;
using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// MaterialComposer: the pipeline-agnostic material-composition primitive. A
// "pass template" shader library owns generic [shader] entry points over a
// surface contract (interface); a "surface" library exports the concrete surface
// type. Composition is slang's own component system (composite + link-time
// specialization) — no generated wrapper modules, no preprocessor stitching:
//
//   shader = composer.ComposeGraphics(gbufferLibrary, mySurfaceLibrary);
//
// Every generic entry point takes the surface type as its first specialization
// argument; value specialization arguments (e.g. the shadow template's
// <let AlphaTest : bool>) feed the entries' value parameters in entry order.
// Composed shaders are cached per (template, surface, type, args, kind), ride
// the module system's disk caches, and hot-reload with it: when either library's
// module is invalidated the shader's caches are cleared and ShaderInvalidated
// fires so consumers can re-record static render bundles.
//
// The composer also owns the material-parameter convention: a surface marks its
// parameter cbuffers with the [MaterialParams] user attribute (free names, any
// number of blocks, scalar/vector float members may mix); the engine discovers
// them from slang's module-level reflection (GetParamsLayouts — no probe
// compile) and packs uniform buffers from named values (PackParamsBuffers).
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Composes pass-template and surface shader libraries into cached, hot-reloadable shaders.</summary>
public sealed class MaterialComposer : IDisposable
{
    /// <summary>The surface type name every surface module exports by convention.</summary>
    public const string DefaultSurfaceTypeName = "Surface";

    /// <summary>
    /// The user-defined attribute a surface marks its material-parameter blocks with
    /// (<c>[MaterialParams] cbuffer ...</c>). Discovery is marker-driven, not
    /// name-driven: a surface names and splits its parameter blocks freely.
    /// </summary>
    public const string ParamsMarkerAttribute = "MaterialParams";

    private readonly record struct CompositionKey(
        ShaderLibrary Template,
        ShaderLibrary Surface,
        string SurfaceType,
        string Specialization,
        string Defines,
        bool Compute);

    private readonly RenderingSystem _rendering;
    private readonly ShaderSystem _shaderSystem;
    private readonly Lock _lock = new();
    private readonly Dictionary<CompositionKey, Shader> _shaders = new();
    private readonly Dictionary<(string Module, string Defines), Dictionary<string, IReadOnlyList<SlangUniformMember>>> _paramLayouts = new();
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
    /// <param name="template">The pass-template library (owns the generic entry points).</param>
    /// <param name="surface">The surface library (exports the surface type).</param>
    /// <param name="valueSpecArgs">Value specialization arguments in entry order (e.g. ["true"] for the shadow template's AlphaTest).</param>
    /// <param name="surfaceType">The companion type name; <see cref="DefaultSurfaceTypeName"/> by convention.</param>
    /// <param name="defines">
    /// Composition-static preprocessor defines (a material asset's surface feature
    /// toggles): baked into the composition identity. Runtime variant switching is
    /// specialization-only — a variant is a distinct composed shader.
    /// </param>
    public Shader ComposeGraphics(
        ShaderLibrary template, ShaderLibrary surface,
        IReadOnlyList<string>? valueSpecArgs = null,
        string surfaceType = DefaultSurfaceTypeName,
        IReadOnlyList<string>? defines = null)
        => Compose(template, surface, valueSpecArgs, surfaceType, compute: false, defines);

    /// <summary>
    /// The composed compute shader of one (template, surface) pair — e.g. the voxel-GI
    /// feed whose template owns a single surface-generic [shader("compute")] entry.
    /// </summary>
    /// <inheritdoc cref="ComposeGraphics"/>
    public Shader ComposeCompute(
        ShaderLibrary template, ShaderLibrary surface,
        IReadOnlyList<string>? valueSpecArgs = null,
        string surfaceType = DefaultSurfaceTypeName,
        IReadOnlyList<string>? defines = null)
        => Compose(template, surface, valueSpecArgs, surfaceType, compute: true, defines);

    /// <summary>
    /// The material-parameter blocks of a surface library — every cbuffer marked
    /// <c>[<see cref="ParamsMarkerAttribute"/>]</c>, with its scalar/vector float
    /// members — from slang's module-level reflection (no entry points, no link).
    /// Cached per (module, defines); empty means the module marks no parameter
    /// blocks. Engine data blocks a surface re-declares (e.g. the per-frame
    /// render data) carry no marker and are never reported.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<SlangUniformMember>> GetParamsLayouts(
        ShaderLibrary surface, IReadOnlyList<string>? defines = null)
    {
        string definesKey = defines == null ? "" : string.Join("|", defines);
        lock (_lock)
        {
            if (_paramLayouts.TryGetValue((surface.Name, definesKey),
                    out Dictionary<string, IReadOnlyList<SlangUniformMember>>? cached))
            {
                return cached;
            }
            Dictionary<string, IReadOnlyList<SlangUniformMember>> lookup = new(StringComparer.Ordinal);
            foreach ((string blockName, List<SlangUniformMember> members) in
                     _shaderSystem.Modules.GetModuleMarkedUniformBlocks(surface.Name, ParamsMarkerAttribute, defines))
            {
                lookup.Add(blockName, members);
            }
            _paramLayouts.Add((surface.Name, definesKey), lookup);
            return lookup;
        }
    }

    /// <summary>
    /// Packs a uniform buffer from a parameter-block layout and named values: members
    /// the value table leaves out read zero; a value reads as many leading components
    /// as the member takes; an unknown name is a typo and fails listing the valid
    /// members. The buffer is laid out at the offsets slang reflected (scalars and
    /// vectors may mix), 16-byte aligned.
    /// </summary>
    /// <param name="layout">The block members (<see cref="GetParamsLayouts"/>).</param>
    /// <param name="values">The values by member name.</param>
    /// <param name="name">The owner name (error context and buffer label).</param>
    public GraphicsBuffer PackParamsBuffer(
        IReadOnlyList<SlangUniformMember> layout,
        IReadOnlyDictionary<string, Vector4> values,
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
            if (!values.TryGetValue(member.Name, out Vector4 value))
            {
                continue;
            }
            if (member.FloatComponentCount > 4)
            {
                throw new InvalidDataException(
                    $"Parameter '{member.Name}' of '{name}' takes {member.FloatComponentCount} components; material parameters support at most 4.");
            }
            for (int i = 0; i < member.FloatComponentCount; i++)
            {
                data[member.OffsetBytes / sizeof(float) + i] = value[i];
            }
        }

        GraphicsArrayBuffer<float> buffer =
            _rendering.CreateGraphicsArrayBuffer<float>(data.Length, $"{name}_params");
        buffer.UpdateBuffer(data.AsSpan());
        return buffer;
    }

    /// <summary>
    /// Packs every parameter block of a surface (see <see cref="GetParamsLayouts"/>)
    /// from one value table: a value whose name matches no member of ANY block is a
    /// typo and fails listing the valid members; the same member name in two blocks
    /// is ambiguous and fails. Blocks without any matching value still pack (their
    /// members read zero).
    /// </summary>
    /// <returns>The packed buffer of each block, keyed by block name.</returns>
    public IReadOnlyDictionary<string, GraphicsBuffer> PackParamsBuffers(
        IReadOnlyDictionary<string, IReadOnlyList<SlangUniformMember>> layouts,
        IReadOnlyDictionary<string, Vector4> values,
        string name)
    {
        List<string> allMembers = [];
        foreach (KeyValuePair<string, IReadOnlyList<SlangUniformMember>> block in layouts)
        {
            foreach (SlangUniformMember member in block.Value)
            {
                allMembers.Add(member.Name);
            }
        }
        foreach (string key in values.Keys)
        {
            if (!allMembers.Contains(key))
            {
                throw new InvalidDataException(
                    $"Parameter '{key}' of '{name}' matches no member of any parameter block of the surface; expected one of: {string.Join(", ", allMembers)}.");
            }
        }
        if (allMembers.Count != allMembers.Distinct().Count())
        {
            string duplicate = allMembers.GroupBy(member => member).First(group => group.Count() > 1).Key;
            throw new InvalidDataException(
                $"Parameter member '{duplicate}' of '{name}' is declared by more than one parameter block of the surface; member names must be unique across blocks.");
        }

        Dictionary<string, GraphicsBuffer> buffers = new(StringComparer.Ordinal);
        try
        {
            foreach (KeyValuePair<string, IReadOnlyList<SlangUniformMember>> block in layouts)
            {
                Dictionary<string, Vector4> blockValues = new(StringComparer.Ordinal);
                foreach (SlangUniformMember member in block.Value)
                {
                    if (values.TryGetValue(member.Name, out Vector4 value))
                    {
                        blockValues[member.Name] = value;
                    }
                }
                buffers.Add(block.Key, PackParamsBuffer(block.Value, blockValues, $"{name}:{block.Key}"));
            }
        }
        catch
        {
            foreach (GraphicsBuffer buffer in buffers.Values)
            {
                buffer.Dispose();
            }
            throw;
        }
        return buffers;
    }

    /// <summary>
    /// The texture slots of one bind group (a shader's material-frequency set):
    /// what the binding side fills from the material's texture table, with the
    /// asset's fallback policy for unbound slots.
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
        ShaderLibrary template, ShaderLibrary surface,
        IReadOnlyList<string>? valueSpecArgs, string surfaceType, bool compute,
        IReadOnlyList<string>? defines)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string[] specArgs = valueSpecArgs == null ? [] : [.. valueSpecArgs];
        string[] staticDefines = defines == null ? [] : [.. defines];
        string specKey = string.Join("|", specArgs);
        CompositionKey key = new(template, surface, surfaceType, specKey,
            string.Join("|", staticDefines), compute);
        lock (_lock)
        {
            if (_shaders.TryGetValue(key, out Shader? cached))
            {
                return cached;
            }

            string shaderName = specArgs.Length == 0
                ? $"{template.Name}+{surface.Name}"
                : $"{template.Name}+{surface.Name}[{specKey}]";
            // The asset's defines are composition-static (baked into the identity);
            // runtime variant switching happens through the composition owner's
            // spec-keyed compositions, never through a material's defines. A
            // composed shader has no open specialization axis of its own — the
            // variant was fixed by the composition — so the handle ignores the
            // accessor-level specialization arguments.
            Shader shader = _rendering.CreateShader(
                shaderName,
                _ => CompilePermutation(key, specArgs, staticDefines, shaderName));
            _shaders.Add(key, shader);
            return shader;
        }
    }

    private ShaderModulesInfo CompilePermutation(
        CompositionKey key, string[] specArgs, string[] staticDefines, string shaderName)
    {
        SlangModuleSystem modules = _shaderSystem.Modules;
        SlangProgram program = modules.GetComposedProgram(
            key.Template.Name, key.Surface.Name, key.SurfaceType, specArgs, staticDefines);

        // Programs stay pinned: ShaderModule structs reference the code arrays.
        lock (_lock)
        {
            _pinnedPrograms.Add(program);
        }

        ShaderModulesInfo info = ShaderSystem.BuildModulesInfo(
            _rendering, modules.Target, shaderName, program);
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
            foreach ((string module, string defines) in _paramLayouts.Keys.ToArray())
            {
                if (affectedModules.Contains(module))
                {
                    _paramLayouts.Remove((module, defines));
                }
            }
            affectedShaders =
            [
                .. _shaders.Where(pair =>
                        affectedModules.Contains(pair.Key.Template.Name) ||
                        affectedModules.Contains(pair.Key.Surface.Name))
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
