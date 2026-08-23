using System.IO;
using System.Text;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;
using Alco.ShaderCompiler;

namespace Alco.World3D;

/// <summary>
/// Compiles data-only <see cref="MaterialAsset"/>s into per-pass GPU materials: a
/// passive registry and cache between assets and <see cref="IMaterialPass"/>es. Passes
/// register their policy (<see cref="RegisterPass"/>) — the standard G-buffer/shadow/
/// glass passes where their renderers are created, feature passes (e.g. the voxel GI's
/// RSM) where the feature is enabled — and each (asset, pass) pair compiles lazily on
/// first request and is reused afterwards, so meshes sharing a material share its GPU
/// materials too.
/// <br/>Every surface — the built-in PbrStandard included — is a Slang module exporting
/// <c>public struct Surface : ISurface</c> (contract: ShadersSlang/Libs/surface.slang).
/// The compiler generates a wrapper module that imports the pass template and the
/// surface module and instantiates the template's generic pass functions with the
/// surface's <c>Surface</c> type — dynamic shader stitching as interface-checked
/// generic instantiation. Wrapper and define-mangled permutations are registered with
/// the engine's shared slang module system (<see cref="ShaderSystem"/>), so composed
/// shaders get its disk caches, dependency tracking and hot reload like any module.
/// The parameter mapping reads Slang's own reflection instead of regex-parsing source
/// (a surface's <c>_materialParams</c> block may mix scalar and vector float members).
/// Composed shaders are cached per (template, surface) and owned by the compiler.
/// <br/>Dispose the compiler to release every compiled material and composed shader; use
/// <see cref="Invalidate"/> when an asset file was hot-reloaded into a new instance.
/// Per-instance data (base color, metallic/roughness, emissive, alpha cutoff) rides the
/// renderers' instance buffers, not the material — renderables read it from the asset.
/// </summary>
public sealed class MaterialCompiler : AutoDisposable
{
    /// <summary>The resource name of a surface's parameter block (see surface.slang).</summary>
    private const string MaterialParamsResource = "_materialParams";

    /// <summary>The asset folder of the Slang pass templates, surface modules and interface library.</summary>
    private const string SlangFolder = "ShadersSlang/";

    /// <summary>The built-in surface every pass composes with when the asset names none.</summary>
    private const string DefaultSurfacePath = SlangFolder + "Materials/pbr-standard.slang";

    /// <summary>
    /// The Slang pass templates by template asset path: a pass composes the named
    /// template (ShadersSlang/Pipelines) with the material's surface module.
    /// </summary>
    private static readonly Dictionary<string, string> SlangTemplates = new(StringComparer.Ordinal)
    {
        [World3DAssetPaths.Shader_GBuffer] = "gbuffer",
        [World3DAssetPaths.Shader_ShadowDepth] = "shadow-depth",
        [World3DAssetPaths.Shader_Rsm] = "rsm",
        [World3DAssetPaths.Shader_ForwardGlass] = "glass",
    };

    /// <summary>Compiled materials, streamed-texture slots and the parameter buffer of one material asset.</summary>
    private sealed class Entry
    {
        public required MaterialAsset Asset { get; init; }
        public Dictionary<string, GraphicsMaterial> Materials { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Texture2D?> Textures { get; } = new(StringComparer.Ordinal);
        public GraphicsBuffer? ParamsBuffer { get; set; }
    }

    private readonly RenderingSystem _rendering;
    private readonly AssetSystem _assets;
    private readonly Dictionary<string, IMaterialPass> _passes = new(StringComparer.Ordinal);
    private readonly Dictionary<MaterialAsset, Entry> _entries = new();
    private readonly Dictionary<(string Template, string Surface), Shader> _slangShaders = new();
    private readonly Dictionary<string, List<SlangUniformMember>> _slangParamLayouts = new(StringComparer.Ordinal);
    private readonly List<SlangProgram> _pinnedPrograms = [];
    private readonly Lock _pinLock = new();

    /// <summary>
    /// Create the compiler. It starts out knowing no passes; register them as their
    /// renderers/features come up (<see cref="RegisterPass"/>).
    /// </summary>
    /// <param name="rendering">The rendering system (material factory, fallback textures, shared ShaderSystem).</param>
    /// <param name="assets">The asset system (template shaders, surface shader sources).</param>
    public MaterialCompiler(RenderingSystem rendering, AssetSystem assets)
    {
        _rendering = rendering;
        _assets = assets;
    }

    /// <summary>
    /// Register a material pass. Pass identifiers must be unique; registering a second
    /// pass under a live id throws.
    /// </summary>
    /// <param name="pass">The pass to register.</param>
    public void RegisterPass(IMaterialPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        if (!_passes.TryAdd(pass.Id, pass))
        {
            throw new ArgumentException($"A material pass '{pass.Id}' is already registered.");
        }
    }

    /// <summary>
    /// The compiled material of an asset for one pass; created on first request, then
    /// cached. The compiler owns the returned material.
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <param name="pass">The registered pass to compile for.</param>
    /// <returns>The compiler-owned material of the (asset, pass) pair.</returns>
    public GraphicsMaterial Get(MaterialAsset asset, IMaterialPass pass)
    {
        ArgumentNullException.ThrowIfNull(pass);
        Entry entry = GetEntry(asset);
        if (!entry.Materials.TryGetValue(pass.Id, out GraphicsMaterial? material))
        {
            // Fill the parameter buffer first so a bad parameter name fails before
            // any material exists, then compile, then bind the buffer by name
            // (TrySet: passes whose template strips the block skip the binding).
            GraphicsBuffer? paramsBuffer = GetParamsBuffer(asset, entry);
            material = pass.Compile(CreateContext(asset));
            if (paramsBuffer != null)
            {
                // TrySet: permutations that dead-strip the block (e.g. the shadow
                // pass consuming only base color alpha) have no such resource.
                material.TrySetBuffer(MaterialParamsResource, paramsBuffer);
            }
            entry.Materials.Add(pass.Id, material);
        }
        return material;
    }

    /// <summary>
    /// The compiled material of an asset for the pass registered under an id, or null
    /// when no such pass exists — e.g. the optional pass of a feature that is disabled
    /// this run.
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <param name="passId">The material-pass identifier.</param>
    /// <returns>The compiler-owned material, or null when the pass is not registered.</returns>
    public GraphicsMaterial? TryGet(MaterialAsset asset, string passId)
    {
        ArgumentNullException.ThrowIfNull(passId);
        return _passes.TryGetValue(passId, out IMaterialPass? pass) ? Get(asset, pass) : null;
    }

    /// <summary>
    /// The pass-template shader composed with the built-in PbrStandard surface: what
    /// renderer constructors receive in place of the retired load-template-as-asset.
    /// Custom surfaces compose their own per-pass shaders through the passes'
    /// <c>ComposeShader</c>; this is only the pipeline-level default.
    /// </summary>
    /// <param name="templatePath">The template asset path (e.g. <see cref="World3DAssetPaths.Shader_GBuffer"/>).</param>
    /// <returns>The compiler-owned composed template shader.</returns>
    public Shader GetTemplateShader(string templatePath)
        => GetSlangShader(templatePath, DefaultSurfacePath);

    /// <summary>
    /// (Re)bind the streamed textures of one asset, by material texture slot (see
    /// <see cref="StandardSurfaceSlotsUtility"/>): stores them as the binding-time values for
    /// not-yet-compiled passes and rebinds every already-compiled pass material (with
    /// the passes' fallback textures for slots still streaming). Render bundles recorded
    /// with the materials must be re-recorded afterwards — call the renderers'
    /// <c>MarkStaticBundleDirty</c>.
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <param name="slots">The streamed textures by material texture slot; null values
    /// mean "still streaming" and keep the fallback.</param>
    public void BindTextures(MaterialAsset asset, IReadOnlyDictionary<string, Texture2D?> slots)
    {
        Entry entry = GetEntry(asset);
        foreach (KeyValuePair<string, Texture2D?> pair in slots)
        {
            entry.Textures[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<string, GraphicsMaterial> pair in entry.Materials)
        {
            if (_passes.TryGetValue(pair.Key, out IMaterialPass? pass))
            {
                pass.RebindTextures(CreateContext(asset), pair.Value, entry.Textures);
            }
        }
    }

    /// <summary>
    /// Drop and dispose the compiled materials of one asset, e.g. after its file was
    /// hot-reloaded into a new <see cref="MaterialAsset"/> instance. The next Get call
    /// recompiles from scratch.
    /// </summary>
    /// <param name="asset">The material asset to invalidate.</param>
    public void Invalidate(MaterialAsset asset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(asset);

        if (!_entries.Remove(asset, out Entry? entry))
        {
            return;
        }
        foreach (GraphicsMaterial material in entry.Materials.Values)
        {
            material.Dispose();
        }
        entry.ParamsBuffer?.Dispose();
    }

    private Entry GetEntry(MaterialAsset asset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        ArgumentNullException.ThrowIfNull(asset);

        if (!_entries.TryGetValue(asset, out Entry? entry))
        {
            entry = new Entry { Asset = asset };
            _entries.Add(asset, entry);
        }
        return entry;
    }

    private MaterialCompileContext CreateContext(MaterialAsset asset)
    {
        return new MaterialCompileContext
        {
            Asset = asset,
            Rendering = _rendering,
            ComposeShader = templatePath => GetShader(asset, templatePath),
        };
    }

    /// <summary>
    /// The pass-template shader for one asset: the template composed with the asset's
    /// surface module, or with the built-in PbrStandard surface when the asset names
    /// none. HLSL surfaces are retired — the composer rejects them with a pointer to
    /// the surface contract.
    /// </summary>
    private Shader GetShader(MaterialAsset asset, string templatePath)
    {
        string surfacePath = asset.SurfaceShader ?? DefaultSurfacePath;
        if (!surfacePath.EndsWith(".slang", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Material '{asset.Name}' names surface '{surfacePath}'; HLSL surfaces were retired by the "
                + "slang migration — port the surface to a .slang module exporting "
                + "'public struct Surface : ISurface' (see ShadersSlang/Libs/surface.slang).");
        }
        return GetSlangShader(templatePath, surfacePath);
    }

    /// <summary>
    /// The composed module shader of one (template, surface) pair, whose
    /// every defines permutation generates a wrapper module through the shared slang
    /// module system (see <see cref="CompileSlangPermutation"/>). Cached per pair.
    /// </summary>
    private Shader GetSlangShader(string templatePath, string surfacePath)
    {
        if (!SlangTemplates.TryGetValue(templatePath, out string? templateStem))
        {
            throw new InvalidDataException(
                $"Pass template '{templatePath}' has no Slang counterpart.");
        }

        string surfaceModule = Path.GetFileNameWithoutExtension(surfacePath);
        (string Template, string Surface) key = (templatePath, surfacePath);
        if (_slangShaders.TryGetValue(key, out Shader? cached))
        {
            return cached;
        }

        Shader shader = _rendering.CreateShader(
            $"{templateStem}+{surfaceModule}",
            defines => CompileSlangPermutation(templateStem, surfacePath, defines));
        _slangShaders.Add(key, shader);
        return shader;
    }

    /// <summary>
    /// Compile one defines permutation of a (template, surface) pair through the shared
    /// slang module system: the surface and template sources are registered with the
    /// permutation's defines prefixed, under define-mangled module names (defines do not
    /// cross module boundaries, so every permutation is a distinct module identity —
    /// plan §8's interim permutation mechanism), and the generated wrapper imports both
    /// and instantiates the template's generic pass functions with the surface's
    /// <c>Surface</c> type. Slang checks the surface against ISurface at instantiation.
    /// </summary>
    private ShaderModulesInfo CompileSlangPermutation(string templateStem, string surfacePath, string[] defines)
    {
        SlangModuleSystem modules = _rendering.ShaderSystem.Modules;
        string templatePath = TemplateAssetPath(templateStem);
        string surfaceModule = Path.GetFileNameWithoutExtension(surfacePath);

        // Import names must be valid snake_case identifiers matching the
        // modules' declarations, so kebab file stems ('pbr-standard') are
        // sanitized ('pbr_standard') before the define mangle suffix forms.
        string surfaceImport = MangleModule(SanitizeModuleName(surfaceModule), defines);
        string templateImport = MangleModule(SanitizeModuleName(templateStem), defines);
        RegisterDefinedModule(modules, surfaceImport, surfacePath, ReadAssetText(surfacePath), defines);
        RegisterDefinedModule(modules, templateImport, templatePath, ReadAssetText(templatePath), defines);

        string wrapperName = PermutationName(templateStem, surfaceModule, defines);
        string wrapper = BuildSlangWrapper(templateStem, templateImport, surfaceImport);
        modules.GetOrLoadModule(wrapperName, $"mat/{wrapperName}", wrapper);

        SlangProgram program = modules.GetProgramAllEntries(wrapperName, []);

        SlangCodeTarget target = modules.Target;
        ShaderModule? vertex = null, fragment = null;
        for (int i = 0; i < program.EntryPoints.Count; i++)
        {
            (string name, int stage) = program.EntryPoints[i];
            ShaderModule module = new(
                SlangCompileSession.SlangStageToEngine(stage),
                target.Language(),
                program.EntryCode[i],
                // slang names every SPIR-V entry point "main" regardless of the
                // source function names (MainVS/MainPS); DXIL containers and MSL
                // libraries keep the declared names.
                target.EntryPointName(name));
            switch (module.Stage)
            {
                case ShaderStage.Vertex:
                    vertex = module;
                    break;
                case ShaderStage.Fragment:
                    fragment = module;
                    break;
                default:
                    throw new NotSupportedException(
                        $"Stage {stage} of entry point '{name}' in composed material shader '{wrapperName}' is not supported.");
            }
        }

        if (vertex is not { } vs || fragment is not { } fs)
        {
            throw new InvalidOperationException(
                $"Composed material shader '{wrapperName}' defines no vertex/fragment entry point pair.");
        }

        // Programs stay pinned: ShaderModule structs reference the SPIR-V arrays.
        lock (_pinLock)
        {
            _pinnedPrograms.Add(program);
        }
        return ShaderModulesInfo.CreateGraphics(wrapperName, defines, vs, fs, program.Reflection);
    }

    /// <summary>
    /// Registers one define-permutation module with the module system: the source gets
    /// the defines prefixed and loads under the (possibly mangled) name with the real
    /// asset path as its dependency identity — file changes on that path invalidate the
    /// permutation like any module. A mangled name is also registered as a virtual
    /// source so the wrapper's import-by-name reaches the permutation.
    /// </summary>
    private static void RegisterDefinedModule(
        SlangModuleSystem modules, string name, string path, string source, string[] defines)
    {
        if (defines.Length > 0)
        {
            source = string.Concat(defines.Select(define => "#define " + define + " 1\n")) + source;
            modules.AddVirtualModule(name, source);
        }
        modules.GetOrLoadModule(name, path, source);
    }

    /// <summary>
    /// The generated wrapper module of one (template, surface permutation): entry points
    /// that instantiate the template's generic pass functions with the surface type.
    /// Slang checks the surface against ISurface at this instantiation.
    /// </summary>
    internal static string BuildSlangWrapper(string templateStem, string templateImport, string surfaceImport)
    {
        string module = $"mat_{SanitizeModuleName(templateImport)}_{SanitizeModuleName(surfaceImport)}";
        return templateStem switch
        {
            "gbuffer" => $$"""
                // Generated by MaterialCompiler: {{templateStem}} + {{surfaceImport}}.
                #language slang 2025
                module {{module}};

                import {{templateImport}};
                import {{surfaceImport}};

                [shader("vertex")]
                GBufferV2F MainVS(GBufferVertex input)
                {
                    return GBufferMainVS<Surface>(input);
                }

                [shader("fragment")]
                void MainPS(GBufferV2F input,
                    out float4 albedoRT : SV_TARGET0,
                    out float4 normalRT : SV_TARGET1,
                    out float4 mrAORT : SV_TARGET2,
                    out float4 emissiveRT : SV_TARGET3)
                {
                    GBufferMainPS<Surface>(input, albedoRT, normalRT, mrAORT, emissiveRT);
                }
                """,
            "shadow-depth" => $$"""
                // Generated by MaterialCompiler: {{templateStem}} + {{surfaceImport}}.
                #language slang 2025
                module {{module}};

                import {{templateImport}};
                import {{surfaceImport}};

                [shader("vertex")]
                ShadowV2F MainVS(ShadowVertex input)
                {
                    return ShadowMainVS<Surface>(input);
                }

                [shader("fragment")]
                void MainPS(ShadowV2F input)
                {
                    ShadowMainPS<Surface>(input);
                }
                """,
            "rsm" => $$"""
                // Generated by MaterialCompiler: {{templateStem}} + {{surfaceImport}}.
                #language slang 2025
                module {{module}};

                import {{templateImport}};
                import {{surfaceImport}};

                [shader("vertex")]
                RsmV2F MainVS(RsmVertex input)
                {
                    return RsmMainVS<Surface>(input);
                }

                [shader("fragment")]
                void MainPS(RsmV2F input,
                    out float4 albedoRT : SV_TARGET0,
                    out float4 normalRT : SV_TARGET1)
                {
                    RsmMainPS<Surface>(input, albedoRT, normalRT);
                }
                """,
            "glass" => $$"""
                // Generated by MaterialCompiler: {{templateStem}} + {{surfaceImport}}.
                #language slang 2025
                module {{module}};

                import {{templateImport}};
                import {{surfaceImport}};

                [shader("vertex")]
                GlassV2F MainVS(GlassVertex input)
                {
                    return GlassMainVS<Surface>(input);
                }

                [shader("fragment")]
                float4 MainPS(GlassV2F input) : SV_TARGET
                {
                    return GlassMainPS<Surface>(input);
                }
                """,
            _ => throw new InvalidDataException($"Unknown Slang pass template '{templateStem}'."),
        };
    }

    private static string TemplateAssetPath(string templateStem)
        => SlangFolder + "Pipelines/" + templateStem + ".slang";

    /// <summary>A module-name-safe form (module names allow word characters only).</summary>
    private static string SanitizeModuleName(string name)
        => string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

    /// <summary>A module name mangled by a defines permutation (FNV-1a of the defines).</summary>
    private static string MangleModule(string moduleName, string[] defines)
    {
        if (defines.Length == 0)
        {
            return moduleName;
        }
        ulong hash = 14695981039346656037;
        foreach (char c in string.Join(';', defines))
        {
            hash ^= c;
            hash *= 1099511628211;
        }
        return $"{moduleName}_d{hash & 0xFFFFFFFF:X8}";
    }

    private static string PermutationName(string templateStem, string surfaceModule, string[] defines)
    {
        return defines.Length == 0
            ? $"{templateStem}+{surfaceModule}"
            : $"{templateStem}+{surfaceModule}+{string.Join("+", defines)}";
    }

    private string ReadAssetText(string path)
    {
        if (!_assets.TryGetStream(path, out Stream? stream))
        {
            throw new InvalidDataException($"Shader source '{path}' was not found in the asset system.");
        }
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The parameter buffer of an asset: the surface's <c>_materialParams</c> block
    /// members, filled from <see cref="MaterialAsset.Parameters"/> by member name —
    /// members the asset leaves out read zero. The compiler packs at the offsets Slang
    /// reflected (scalars and vectors may mix). Created on first compile, reused by
    /// every pass material of the asset.
    /// </summary>
    private GraphicsBuffer? GetParamsBuffer(MaterialAsset asset, Entry entry)
    {
        if (asset.SurfaceShader == null)
        {
            // The built-in surface declares no parameter block.
            if (asset.Parameters.Count > 0)
            {
                throw new InvalidDataException(
                    $"Material '{asset.Name}' has parameters, but the built-in PbrStandard surface declares no _materialParams block.");
            }
            return null;
        }

        List<SlangUniformMember> members = GetSlangParamLayout(asset.SurfaceShader);
        if (members.Count == 0)
        {
            if (asset.Parameters.Count > 0)
            {
                throw new InvalidDataException(
                    $"Material '{asset.Name}' has parameters, but its surface '{asset.SurfaceShader}' declares no _materialParams block.");
            }
            return null;
        }

        entry.ParamsBuffer ??= CreateSlangParamsBuffer(asset, members);
        return entry.ParamsBuffer;
    }

    /// <summary>
    /// The members of a Slang surface's <c>_materialParams</c> block, from Slang's own
    /// reflection: a probe compile of the G-buffer wrapper (no defines) whose layout is
    /// the block's declaration. Cached per surface path; empty means no block.
    /// </summary>
    internal List<SlangUniformMember> GetSlangParamLayout(string surfacePath)
    {
        if (_slangParamLayouts.TryGetValue(surfacePath, out List<SlangUniformMember>? cached))
        {
            return cached;
        }

        SlangModuleSystem modules = _rendering.ShaderSystem.Modules;
        // Sanitized to the declared snake_case module name ('pbr-standard' file
        // imports as 'pbr_standard') so the probe wrapper's import parses.
        string surfaceModule = SanitizeModuleName(Path.GetFileNameWithoutExtension(surfacePath));
        RegisterDefinedModule(modules, surfaceModule, surfacePath, ReadAssetText(surfacePath), Array.Empty<string>());

        string wrapperName = $"{surfaceModule}+params";
        modules.GetOrLoadModule(
            wrapperName, $"mat/{wrapperName}", BuildSlangWrapper("gbuffer", "gbuffer", surfaceModule));
        SlangProgram program = modules.GetProgramAllEntries(wrapperName, []);
        lock (_pinLock)
        {
            _pinnedPrograms.Add(program);
        }

        List<SlangUniformMember> members =
            program.GetUniformMembers(MaterialParamsResource);
        _slangParamLayouts.Add(surfacePath, members);
        return members;
    }

    private GraphicsBuffer CreateSlangParamsBuffer(MaterialAsset asset, List<SlangUniformMember> members)
    {
        // An unknown parameter name is a typo in the asset: fail listing the valid ones.
        foreach (string name in asset.Parameters.Keys)
        {
            if (members.All(member => member.Name != name))
            {
                throw new InvalidDataException(
                    $"Material '{asset.Name}' parameter '{name}' matches no _materialParams member of '{asset.SurfaceShader}'; expected one of: {string.Join(", ", members.Select(member => member.Name))}.");
            }
        }

        uint sizeBytes = 0;
        foreach (SlangUniformMember member in members)
        {
            sizeBytes = Math.Max(sizeBytes, member.OffsetBytes + member.SizeBytes);
        }
        sizeBytes = (sizeBytes + 15u) & ~15u;

        float[] data = new float[sizeBytes / sizeof(float)];
        foreach (SlangUniformMember member in members)
        {
            if (!asset.Parameters.TryGetValue(member.Name, out float[]? components))
            {
                continue;
            }
            if (components.Length > member.FloatComponentCount)
            {
                throw new InvalidDataException(
                    $"Material '{asset.Name}' parameter '{member.Name}' has {components.Length} components, but the surface member takes {member.FloatComponentCount}.");
            }
            for (int i = 0; i < components.Length; i++)
            {
                data[member.OffsetBytes / sizeof(float) + i] = components[i];
            }
        }

        GraphicsArrayBuffer<float> buffer =
            _rendering.CreateGraphicsArrayBuffer<float>(data.Length, $"{asset.Name}_params");
        buffer.UpdateBuffer(data.AsSpan());
        return buffer;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (Entry entry in _entries.Values)
            {
                foreach (GraphicsMaterial material in entry.Materials.Values)
                {
                    material.Dispose();
                }
                entry.ParamsBuffer?.Dispose();
            }
            _entries.Clear();

            foreach (Shader shader in _slangShaders.Values)
            {
                shader.Dispose();
            }
            _slangShaders.Clear();

            lock (_pinLock)
            {
                _pinnedPrograms.Clear();
            }
        }
    }
}
