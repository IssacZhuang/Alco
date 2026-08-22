using System.IO;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Alco.Graphics;
using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Compiles data-only <see cref="MaterialAsset"/>s into per-pass GPU materials: a
/// passive registry and cache between assets and <see cref="IMaterialPass"/>es. Passes
/// register their policy (<see cref="RegisterPass"/>) — the standard G-buffer/shadow/
/// glass passes where their renderers are created, feature passes (e.g. the voxel GI's
/// RSM) where the feature is enabled — and each (asset, pass) pair compiles lazily on
/// first request and is reused afterwards, so meshes sharing a material share its GPU
/// materials too.
/// <br/>Assets naming no <see cref="MaterialAsset.SurfaceShader"/> evaluate the built-in
/// PbrStandard surface: their pass shaders are the templates as shipped, loaded through
/// the asset system. Assets naming an HLSL surface (<c>.hlsli</c>) are composed per
/// template by splicing the surface into the template's <c>@SURFACE@</c> line and
/// resolving includes (DXC path). Assets naming a Slang surface (<c>.slang</c>) are
/// composed by Slang instead: the compiler generates a wrapper translation unit that
/// #includes the pass template and imports the surface module, instantiating the
/// template's generic pass functions with the surface's <c>Surface</c> type — dynamic
/// shader stitching becomes interface-checked generic instantiation, and the dynamic
/// parameter mapping reads Slang's own reflection instead of regex-parsing the source
/// (a surface's <c>_materialParams</c> block may mix scalar and vector float members;
/// see ShadersSlang/Libs/surface.slang). Both composed-shader kinds are cached per
/// (template, surface) and owned by the compiler.
/// <br/>Dispose the compiler to release every compiled material and composed shader; use
/// <see cref="Invalidate"/> when an asset file was hot-reloaded into a new instance.
/// Per-instance data (base color, metallic/roughness, emissive, alpha cutoff) rides the
/// renderers' instance buffers, not the material — renderables read it from the asset.
/// </summary>
public sealed class MaterialCompiler : AutoDisposable
{
    /// <summary>The marker comment identifying a template's swappable surface include line.</summary>
    private const string SurfaceMarker = "@SURFACE@";

    /// <summary>The resource name of a surface's parameter block (see Surface.hlsli and surface.slang).</summary>
    private const string MaterialParamsResource = "_materialParams";

    /// <summary>The asset folder of the Slang pass templates, surface modules and interface library.</summary>
    private const string SlangFolder = "ShadersSlang/";

    /// <summary>
    /// The Slang pass templates by HLSL template asset path: a Slang surface composed
    /// with one of these passes compiles the named template (ShadersSlang/Pipelines)
    /// instead of splicing HLSL. The glass pass keeps its HLSL-only composition for now.
    /// </summary>
    private static readonly Dictionary<string, string> SlangTemplates = new(StringComparer.Ordinal)
    {
        [World3DAssetPaths.Shader_GBuffer] = "gbuffer",
        [World3DAssetPaths.Shader_ShadowDepth] = "shadow_depth",
        [World3DAssetPaths.Shader_Rsm] = "rsm",
    };

    /// <summary>The Slang source folders a module/import/include name is looked up in.</summary>
    private static readonly string[] SlangSourceFolders = ["Pipelines/", "Materials/", "Libs/"];

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
    private readonly Dictionary<(string Template, string Surface), Shader> _composedShaders = new();
    private readonly Dictionary<string, string[]> _paramLayouts = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Template, string Surface), Shader> _slangShaders = new();
    private readonly Dictionary<string, List<SlangUniformMember>> _slangParamLayouts = new(StringComparer.Ordinal);
    private readonly SlangShaderCompiler _slang = new();

    /// <summary>
    /// Create the compiler. It starts out knowing no passes; register them as their
    /// renderers/features come up (<see cref="RegisterPass"/>).
    /// </summary>
    /// <param name="rendering">The rendering system (material factory, fallback textures).</param>
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
        if (_entries.Remove(asset, out Entry? entry))
        {
            foreach (GraphicsMaterial material in entry.Materials.Values)
            {
                material.Dispose();
            }
            entry.ParamsBuffer?.Dispose();
        }
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
    /// The pass-template shader for one asset: the template asset as shipped (built-in
    /// PbrStandard surface included) when the asset names no surface, the template
    /// composed with the asset's surface otherwise — via Slang for <c>.slang</c>
    /// surfaces, via the DXC splice for <c>.hlsli</c> ones.
    /// </summary>
    private Shader GetShader(MaterialAsset asset, string templatePath)
    {
        if (asset.SurfaceShader == null)
        {
            return _assets.Load<Shader>(templatePath);
        }
        if (asset.SurfaceShader.EndsWith(".slang", StringComparison.OrdinalIgnoreCase))
        {
            return GetSlangShader(asset, templatePath);
        }
        return GetHlslShader(asset, templatePath);
    }

    /// <summary>
    /// The composed shader of one Slang (template, surface) pair: a provider-backed
    /// shader whose every defines permutation compiles a generated wrapper through
    /// Slang (see <see cref="CompileSlangPermutation"/>). Cached per pair.
    /// </summary>
    private Shader GetSlangShader(MaterialAsset asset, string templatePath)
    {
        if (!SlangTemplates.TryGetValue(templatePath, out string? templateStem))
        {
            throw new InvalidDataException(
                $"Pass template '{templatePath}' has no Slang counterpart; Slang surfaces support the "
                + "G-buffer, shadow and RSM passes — use an HLSL surface for this pass.");
        }

        string surfacePath = asset.SurfaceShader!;
        (string Template, string Surface) key = (templatePath, surfacePath);
        if (_slangShaders.TryGetValue(key, out Shader? cached))
        {
            return cached;
        }

        string surfaceModule = Path.GetFileNameWithoutExtension(surfacePath);
        Shader shader = _rendering.CreateShader(
            $"{templateStem}+{surfaceModule}",
            defines => CompileSlangPermutation(templateStem, surfacePath, defines));
        _slangShaders.Add(key, shader);
        return shader;
    }

    /// <summary>
    /// Compile one defines permutation of a Slang (template, surface) pair: the wrapper
    /// translation unit #includes the pass template (so the defines apply to it, exactly
    /// like the DXC splice) and imports the surface module under a define-mangled name
    /// (Slang caches modules per session by path; the mangling makes every permutation
    /// import the surface with its own defines prepended), then instantiates the
    /// template's generic pass functions with the surface's <c>Surface</c> type.
    /// </summary>
    private ShaderModulesInfo CompileSlangPermutation(string templateStem, string surfacePath, string[] defines)
    {
        string surfaceModule = Path.GetFileNameWithoutExtension(surfacePath);
        string wrapper = BuildSlangWrapper(templateStem, MangleSurfaceModule(surfaceModule, defines));
        SlangCompiledShader compiled = _slang.CompileGraphics(
            PermutationName(templateStem, surfaceModule, defines),
            wrapper,
            defines.Select(define => (define, "1")).ToArray(),
            [MaterialParamsResource],
            path => ResolveSlangFile(path, surfacePath, defines));

        // Slang names the emitted SPIR-V entry points "main" regardless of the
        // source function names (MainVS/MainPS).
        ShaderModule vertex = new(ShaderStage.Vertex, ShaderLanguage.SPIRV, compiled.VertexSpirv, "main");
        ShaderModule fragment = new(ShaderStage.Fragment, ShaderLanguage.SPIRV, compiled.FragmentSpirv, "main");
        return ShaderModulesInfo.CreateGraphics(
            PermutationName(templateStem, surfaceModule, defines), defines, vertex, fragment, compiled.Reflection);
    }

    /// <summary>
    /// The generated wrapper translation unit of one (template, surface permutation):
    /// entry points that instantiate the template's generic pass functions with the
    /// surface type. Slang checks the surface against ISurface at this instantiation.
    /// </summary>
    internal static string BuildSlangWrapper(string templateStem, string surfaceModule) => templateStem switch
    {
        "gbuffer" => $$"""
            // Generated by MaterialCompiler: {{templateStem}} + {{surfaceModule}}.
            #include "gbuffer.slang"
            import {{surfaceModule}};

            [shader("vertex")]
            GBufferV2F MainVS(GBufferVertex input)
            {
                return GBufferMainVS<Surface>(input);
            }

            [shader("pixel")]
            void MainPS(GBufferV2F input,
                out float4 albedoRT : SV_TARGET0,
                out float4 normalRT : SV_TARGET1,
                out float4 mrAORT : SV_TARGET2,
                out float4 emissiveRT : SV_TARGET3)
            {
                GBufferMainPS<Surface>(input, albedoRT, normalRT, mrAORT, emissiveRT);
            }
            """,
        "shadow_depth" => $$"""
            // Generated by MaterialCompiler: {{templateStem}} + {{surfaceModule}}.
            #include "shadow_depth.slang"
            import {{surfaceModule}};

            [shader("vertex")]
            ShadowV2F MainVS(ShadowVertex input)
            {
                return ShadowMainVS<Surface>(input);
            }

            [shader("pixel")]
            void MainPS(ShadowV2F input)
            {
                ShadowMainPS<Surface>(input);
            }
            """,
        "rsm" => $$"""
            // Generated by MaterialCompiler: {{templateStem}} + {{surfaceModule}}.
            #include "rsm.slang"
            import {{surfaceModule}};

            [shader("vertex")]
            RsmV2F MainVS(RsmVertex input)
            {
                return RsmMainVS<Surface>(input);
            }

            [shader("pixel")]
            void MainPS(RsmV2F input,
                out float4 albedoRT : SV_TARGET0,
                out float4 normalRT : SV_TARGET1)
            {
                RsmMainPS<Surface>(input, albedoRT, normalRT);
            }
            """,
        _ => throw new InvalidDataException($"Unknown Slang pass template '{templateStem}'."),
    };

    /// <summary>
    /// Serve one Slang module/import/include path: the define-mangled surface module
    /// resolves to the surface's source with the permutation defines prepended;
    /// everything else resolves by file name in the Slang source folders. Returns null
    /// for unknown paths (Slang reports them as missing).
    /// </summary>
    private string? ResolveSlangFile(string path, string surfacePath, string[] defines)
    {
        string stem = Path.GetFileNameWithoutExtension(path);
        string surfaceModule = Path.GetFileNameWithoutExtension(surfacePath);
        if (stem == MangleSurfaceModule(surfaceModule, defines))
        {
            StringBuilder source = new();
            foreach (string define in defines)
            {
                source.AppendLine($"#define {define} 1");
            }
            return source.Append(ReadAssetText(surfacePath)).ToString();
        }

        string fileName = Path.GetFileName(path);
        foreach (string folder in SlangSourceFolders)
        {
            if (_assets.TryGetStream(SlangFolder + folder + fileName, out Stream? stream))
            {
                using StreamReader reader = new(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
        }
        return null;
    }

    /// <summary>The surface module name mangled by a defines permutation (FNV-1a of the defines).</summary>
    private static string MangleSurfaceModule(string surfaceModule, string[] defines)
    {
        if (defines.Length == 0)
        {
            return surfaceModule;
        }
        ulong hash = 14695981039346656037;
        foreach (char c in string.Join(';', defines))
        {
            hash ^= c;
            hash *= 1099511628211;
        }
        return $"{surfaceModule}_d{hash & 0xFFFFFFFF:X8}";
    }

    private static string PermutationName(string templateStem, string surfaceModule, string[] defines)
    {
        return defines.Length == 0
            ? $"{templateStem}+{surfaceModule}"
            : $"{templateStem}+{surfaceModule}+{string.Join("+", defines)}";
    }

    /// <summary>
    /// The composed shader of one HLSL (template, surface) pair (DXC splice path).
    /// Cached per pair; the compiler owns the shader.
    /// </summary>
    private Shader GetHlslShader(MaterialAsset asset, string templatePath)
    {
        string surfacePath = asset.SurfaceShader!;
        (string Template, string Surface) key = (templatePath, surfacePath);
        if (_composedShaders.TryGetValue(key, out Shader? cached))
        {
            return cached;
        }

        string templateText = ReadAssetText(templatePath);
        string surfaceText = ReadAssetText(surfacePath);
        string composed = ReplaceSurfaceLine(templateText, surfaceText, templatePath);
        string resolved = new IncludeHelper().ProcessInclude(composed, templatePath, ReadAssetText);
        string name = $"{Path.GetFileNameWithoutExtension(templatePath)}+{Path.GetFileNameWithoutExtension(surfacePath)}";
        Shader shader = _rendering.CreateShader(resolved, name);
        _composedShaders.Add(key, shader);
        return shader;
    }

    /// <summary>
    /// Splice a surface shader into a template: the template line carrying the
    /// <see cref="SurfaceMarker"/> comment (its default-surface include) is replaced by
    /// the surface's source text.
    /// </summary>
    private static string ReplaceSurfaceLine(string templateText, string surfaceText, string templatePath)
    {
        StringBuilder builder = new();
        using StringReader reader = new(templateText);
        string? line;
        bool replaced = false;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Contains(SurfaceMarker))
            {
                builder.AppendLine(surfaceText);
                replaced = true;
            }
            else
            {
                builder.AppendLine(line);
            }
        }
        if (!replaced)
        {
            throw new InvalidDataException($"Pass template '{templatePath}' has no '{SurfaceMarker}' surface line to swap.");
        }
        return builder.ToString();
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
    /// members the asset leaves out read zero. Slang surfaces pack at the offsets
    /// Slang reflected (scalars and vectors may mix); HLSL surfaces keep the one
    /// float4-per-member register convention. Created on first compile, reused by
    /// every pass material of the asset.
    /// </summary>
    private GraphicsBuffer? GetParamsBuffer(MaterialAsset asset, Entry entry)
    {
        bool slangSurface = asset.SurfaceShader != null &&
            asset.SurfaceShader.EndsWith(".slang", StringComparison.OrdinalIgnoreCase);
        if (asset.SurfaceShader == null || !slangSurface)
        {
            return GetHlslParamsBuffer(asset, entry);
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
    private List<SlangUniformMember> GetSlangParamLayout(string surfacePath)
    {
        if (_slangParamLayouts.TryGetValue(surfacePath, out List<SlangUniformMember>? cached))
        {
            return cached;
        }

        string surfaceModule = Path.GetFileNameWithoutExtension(surfacePath);
        SlangCompiledShader probe = _slang.CompileGraphics(
            $"{surfaceModule}+params",
            BuildSlangWrapper("gbuffer", surfaceModule),
            [],
            [MaterialParamsResource],
            path => ResolveSlangFile(path, surfacePath, []));

        List<SlangUniformMember> members =
            probe.UniformMembers.TryGetValue(MaterialParamsResource, out List<SlangUniformMember>? layout)
                ? layout
                : [];
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

    /// <summary>
    /// The parameter buffer of an asset with an HLSL surface (or none): one float4
    /// register per member of the surface's <c>_materialParams</c> block (declaration
    /// order). Created on first compile, reused by every pass material of the asset.
    /// </summary>
    private GraphicsBuffer? GetHlslParamsBuffer(MaterialAsset asset, Entry entry)
    {
        if (asset.SurfaceShader == null || GetParamLayout(asset.SurfaceShader).Length == 0)
        {
            if (asset.Parameters.Count > 0)
            {
                throw new InvalidDataException(
                    $"Material '{asset.Name}' has parameters, but its surface ({asset.SurfaceShader ?? "the built-in PbrStandard"}) declares no _materialParams block.");
            }
            return null;
        }

        entry.ParamsBuffer ??= CreateParamsBuffer(asset, GetParamLayout(asset.SurfaceShader));
        return entry.ParamsBuffer;
    }

    private GraphicsBuffer CreateParamsBuffer(MaterialAsset asset, string[] members)
    {
        // An unknown parameter name is a typo in the asset: fail listing the valid ones.
        foreach (string name in asset.Parameters.Keys)
        {
            if (!members.Contains(name))
            {
                throw new InvalidDataException(
                    $"Material '{asset.Name}' parameter '{name}' matches no _materialParams member of '{asset.SurfaceShader}'; expected one of: {string.Join(", ", members)}.");
            }
        }

        Vector4[] registers = new Vector4[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            if (asset.Parameters.TryGetValue(members[i], out float[]? components))
            {
                registers[i] = new Vector4(
                    components[0],
                    components.Length > 1 ? components[1] : 0.0f,
                    components.Length > 2 ? components[2] : 0.0f,
                    components.Length > 3 ? components[3] : 0.0f);
            }
        }

        GraphicsArrayBuffer<Vector4> buffer =
            _rendering.CreateGraphicsArrayBuffer<Vector4>(registers.Length, $"{asset.Name}_params");
        buffer.UpdateBuffer(registers.AsSpan());
        return buffer;
    }

    /// <summary>
    /// The member names of an HLSL surface's <c>_materialParams</c> block, in declaration
    /// order (one float4 register each — the packing convention of Shaders/Libs/Surface.hlsli).
    /// Cached per surface path; empty means the surface declares no block.
    /// </summary>
    private string[] GetParamLayout(string surfacePath)
    {
        if (_paramLayouts.TryGetValue(surfacePath, out string[]? cached))
        {
            return cached;
        }

        Match block = Regex.Match(
            ReadAssetText(surfacePath),
            @"DEFINE_UNIFORM\(\s*\d+\s*,\s*_materialParams\s*\)\s*\{([^}]*)\}",
            RegexOptions.CultureInvariant);
        List<string> names = new();
        if (block.Success)
        {
            // Drop line comments first: a member may carry a trailing "// ..." note.
            string body = Regex.Replace(block.Groups[1].Value, @"//[^\n]*", "", RegexOptions.CultureInvariant);
            foreach (string raw in body.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string statement = raw.Trim();
                if (statement.Length == 0)
                {
                    continue;
                }
                Match member = Regex.Match(statement, @"^float4\s+([A-Za-z_]\w*)$", RegexOptions.CultureInvariant);
                if (!member.Success)
                {
                    throw new InvalidDataException(
                        $"Surface '{surfacePath}' _materialParams member '{statement}' must be a bare 'float4 name;' declaration — "
                        + "one register per member is what keeps the material parameter mapping in step with HLSL packing.");
                }
                names.Add(member.Groups[1].Value);
            }
        }

        string[] members = [.. names];
        _paramLayouts.Add(surfacePath, members);
        return members;
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

            foreach (Shader shader in _composedShaders.Values)
            {
                shader.Dispose();
            }
            _composedShaders.Clear();

            foreach (Shader shader in _slangShaders.Values)
            {
                shader.Dispose();
            }
            _slangShaders.Clear();

            _slang.Dispose();
        }
    }
}
