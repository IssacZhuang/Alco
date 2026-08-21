using System.IO;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
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
/// the asset system. Assets naming a surface shader are composed per template by
/// splicing the surface into the template's <c>@SURFACE@</c> line and resolving includes;
/// the composed shaders are cached per (template, surface) and owned by the compiler.
/// <br/>Dispose the compiler to release every compiled material and composed shader; use
/// <see cref="Invalidate"/> when an asset file was hot-reloaded into a new instance.
/// Per-instance data (base color, metallic/roughness, emissive, alpha cutoff) rides the
/// renderers' instance buffers, not the material — renderables read it from the asset.
/// </summary>
public sealed class MaterialCompiler : AutoDisposable
{
    /// <summary>The marker comment identifying a template's swappable surface include line.</summary>
    private const string SurfaceMarker = "@SURFACE@";

    /// <summary>The resource name of a surface's parameter block (see Surface.hlsli).</summary>
    private const string MaterialParamsResource = "_materialParams";

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
    /// PbrStandard surface included) when the asset names no surface, the composed
    /// template+surface shader otherwise.
    /// </summary>
    private Shader GetShader(MaterialAsset asset, string templatePath)
    {
        if (asset.SurfaceShader == null)
        {
            return _assets.Load<Shader>(templatePath);
        }

        (string Template, string Surface) key = (templatePath, asset.SurfaceShader);
        if (_composedShaders.TryGetValue(key, out Shader? cached))
        {
            return cached;
        }

        string templateText = ReadAssetText(templatePath);
        string surfaceText = ReadAssetText(asset.SurfaceShader);
        string composed = ReplaceSurfaceLine(templateText, surfaceText, templatePath);
        string resolved = new IncludeHelper().ProcessInclude(composed, templatePath, ReadAssetText);
        string name = $"{Path.GetFileNameWithoutExtension(templatePath)}+{Path.GetFileNameWithoutExtension(asset.SurfaceShader)}";
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
    /// The parameter buffer of an asset: one float4 register per member of the surface's
    /// <c>_materialParams</c> block (declaration order), filled from
    /// <see cref="MaterialAsset.Parameters"/> by member name — members the asset leaves
    /// out read zero. Created on first compile, reused by every pass material of the asset.
    /// </summary>
    private GraphicsBuffer? GetParamsBuffer(MaterialAsset asset, Entry entry)
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
    /// The member names of a surface's <c>_materialParams</c> block, in declaration order
    /// (one float4 register each — the packing convention of Shaders/Libs/Surface.hlsli).
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
        }
    }
}
