using System.IO;
using Alco.Graphics;
using Alco.Rendering;
using Alco.ShaderCompiler;

namespace Alco.World3D;

/// <summary>
/// Compiles data-only <see cref="MaterialAsset"/>s into per-pass GPU materials: a
/// passive registry and cache between assets and material passes. Passes register
/// as data (<see cref="MaterialPassDesc"/> — the standard G-buffer/shadow/glass
/// passes where their renderers are created, feature passes like the voxel GI's
/// RSM where the feature is enabled, game-defined passes anywhere), and each
/// (asset, pass) pair compiles lazily on first request, so meshes sharing a
/// material share its GPU materials too.
/// <br/>Every surface — the built-in PbrStandard included — is a Slang module
/// exporting <c>public struct Surface : ISurface</c> (contract:
/// Shaders/Libs/alco-world3d-surface.slang). Compilation is slang's own component
/// system: the pass template owns the surface-generic <c>[shader]</c> entry
/// points and composes with the surface module directly (composite + link-time
/// specialization, no generated wrapper modules, no preprocessor stitching) —
/// see <see cref="MaterialComposer"/>, the pipeline-agnostic composition core
/// this class builds its asset policy on. Value specialization replaces
/// pass-private defines (the shadow pass's alpha test is the template's
/// <c>let AlphaTest : bool</c> parameter, fed from <see cref="MaterialAsset.AlphaMode"/>).
/// <br/>The parameter mapping reads slang's module-level reflection (a surface's
/// <c>[MaterialParams]</c>-marked blocks may mix scalar and vector float members,
/// under any block names); texture
/// slots are validated against the composed reflection at compile time and bound
/// by name with pattern fallbacks (<c>_normal*</c> → flat normal,
/// <c>_emissive*</c> → black, everything else → white).
/// <br/>Dispose the compiler to release every compiled material and composed
/// shader; use <see cref="Invalidate"/> when an asset file was hot-reloaded into
/// a new instance. Per-instance data (base color, metallic/roughness, emissive,
/// alpha cutoff) rides the renderers' instance buffers, not the material.
/// </summary>
public sealed class MaterialCompiler : AutoDisposable
{
    /// <summary>The asset path of the built-in surface every pass composes with when the asset names none.</summary>
    public const string DefaultSurfacePath = "Shaders/Materials/pbr-standard.slang";

    /// <summary>The descriptor set index the surface contract reserves for surface resources.</summary>
    public const int SurfaceResourceSet = 2;

    /// <summary>Compiled materials, streamed-texture slots and the parameter buffers of one material asset.</summary>
    private sealed class Entry
    {
        public required MaterialAsset Asset { get; init; }
        public Dictionary<string, GraphicsMaterial> Materials { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Texture2D?> Textures { get; } = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, GraphicsBuffer>? ParamsBuffers { get; set; }
    }

    private readonly RenderingSystem _rendering;
    private readonly Dictionary<string, MaterialPassDesc> _passes = new(StringComparer.Ordinal);
    private readonly Dictionary<MaterialAsset, Entry> _entries = new();
    private Texture2D? _flatNormalTexture;
    private MaterialAsset? _defaultAsset;

    /// <summary>
    /// Create the compiler. It starts out knowing no passes; register them as their
    /// renderers/features come up (<see cref="RegisterPass"/>).
    /// </summary>
    /// <param name="rendering">The rendering system (material factory, fallback textures, shared ShaderSystem).</param>
    public MaterialCompiler(RenderingSystem rendering)
    {
        _rendering = rendering;
        Composer = new MaterialComposer(rendering);
    }

    /// <summary>
    /// The pipeline-agnostic composition core (template×surface shaders, parameter
    /// layouts, parameter packing). Facilities composing outside the graphics pass
    /// registry — e.g. the voxel GI's compute feed — use it directly.
    /// </summary>
    public MaterialComposer Composer { get; }

    /// <summary>The shared asset selecting the built-in PbrStandard surface, for pipeline-level defaults.</summary>
    public MaterialAsset DefaultAsset => _defaultAsset ??= new MaterialAsset { Name = "pbr_standard" };

    /// <summary>The 1x1 flat-normal fallback texture of the <c>_normal*</c> slots (decodes to the identity tangent-space normal).</summary>
    public Texture2D FlatNormalTexture => _flatNormalTexture ??= CreateFlatNormalTexture();

    /// <summary>
    /// Register a material pass. Pass identifiers must be unique; registering a second
    /// pass under a live id throws.
    /// </summary>
    /// <param name="desc">The pass descriptor to register.</param>
    public void RegisterPass(MaterialPassDesc desc)
    {
        ArgumentNullException.ThrowIfNull(desc);
        if (!_passes.TryAdd(desc.Id, desc))
        {
            throw new ArgumentException($"A material pass '{desc.Id}' is already registered.");
        }
    }

    /// <summary>
    /// Whether a pass is registered and accepts the asset — the pass-participation
    /// routing that replaces game-side alpha-mode special cases.
    /// </summary>
    public bool Accepts(MaterialAsset asset, string passId)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return _passes.TryGetValue(passId, out MaterialPassDesc? desc)
            && (desc.Accepts?.Invoke(asset) ?? true);
    }

    /// <summary>
    /// The compiled material of an asset for the pass registered under an id; created
    /// on first request, then cached. The compiler owns the returned material.
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <param name="passId">The registered pass identifier.</param>
    /// <returns>The compiler-owned material of the (asset, pass) pair.</returns>
    /// <exception cref="ArgumentException">No pass is registered under <paramref name="passId"/>.</exception>
    /// <exception cref="InvalidDataException">The pass does not accept the asset.</exception>
    public GraphicsMaterial Get(MaterialAsset asset, string passId)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (!_passes.TryGetValue(passId, out MaterialPassDesc? desc))
        {
            throw new ArgumentException($"No material pass '{passId}' is registered.", nameof(passId));
        }
        if (desc.Accepts != null && !desc.Accepts(asset))
        {
            throw new InvalidDataException(
                $"Material pass '{passId}' does not accept material '{asset.Name}'.");
        }

        Entry entry = GetEntry(asset);
        if (!entry.Materials.TryGetValue(passId, out GraphicsMaterial? material))
        {
            material = Compile(asset, desc, entry);
            entry.Materials.Add(passId, material);
        }
        return material;
    }

    /// <summary>
    /// The compiled material of an asset for the pass registered under an id, or null
    /// when no such pass exists or the pass does not accept the asset — e.g. the
    /// optional pass of a feature that is disabled this run, or the glass pass for an
    /// opaque material.
    /// </summary>
    public GraphicsMaterial? TryGet(MaterialAsset asset, string passId)
        => Accepts(asset, passId) ? Get(asset, passId) : null;

    /// <summary>
    /// The shader of one pass template composed with an asset's surface (the built-in
    /// PbrStandard surface when <paramref name="asset"/> is null or names none): what
    /// renderer constructors receive as their pipeline-level default shader. Custom
    /// passes go through <see cref="Composer"/> for non-graphics stage mixes.
    /// </summary>
    /// <param name="asset">The material asset whose surface composes; null selects the built-in surface.</param>
    /// <param name="templateModule">The pass-template module name.</param>
    /// <param name="valueSpecArgs">Value specialization arguments in entry order.</param>
    public Shader ComposeSurfaceShader(
        MaterialAsset? asset, string templateModule, IReadOnlyList<string>? valueSpecArgs = null)
        => Composer.ComposeGraphics(
            templateModule, SurfaceModuleName(asset), valueSpecArgs, defines: asset?.Defines);

    /// <summary>
    /// The compute counterpart of <see cref="ComposeSurfaceShader"/>, for facilities
    /// whose surface feed is a compute pass (e.g. the voxel GI's voxelization).
    /// </summary>
    /// <param name="asset">The material asset whose surface composes; null selects the built-in surface.</param>
    /// <param name="templateModule">The pass-template module name.</param>
    /// <param name="valueSpecArgs">Value specialization arguments in entry order.</param>
    public Shader ComposeSurfaceComputeShader(
        MaterialAsset? asset, string templateModule, IReadOnlyList<string>? valueSpecArgs = null)
        => Composer.ComposeCompute(
            templateModule, SurfaceModuleName(asset), valueSpecArgs, defines: asset?.Defines);

    /// <summary>
    /// The fallback texture of one surface texture resource (<c>_normal*</c> → flat
    /// normal, <c>_emissive*</c> → black, everything else → white) — the same policy
    /// the compiler applies when binding asset textures. Facilities composing surface
    /// feeds outside the graphics pass registry (e.g. the voxel GI) bind through this.
    /// </summary>
    public Texture2D GetFallbackTexture(string resourceName) => FallbackFor(resourceName);

    /// <summary>
    /// The slang module name of an asset's surface (the built-in PbrStandard module
    /// when the asset names none): the file stem with module-name characters
    /// ('pbr-standard' → 'pbr_standard', matching the source's module declaration).
    /// </summary>
    public static string SurfaceModuleName(MaterialAsset? asset)
    {
        string stem = Path.GetFileNameWithoutExtension(asset?.SurfaceShader ?? DefaultSurfacePath);
        return string.Concat(stem.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
    }

    /// <summary>
    /// (Re)bind the streamed textures of one asset, by material texture slot (slot
    /// name = shader resource name without the leading underscore): stores them as
    /// the binding-time values for not-yet-compiled passes and rebinds every
    /// already-compiled pass material (with the pattern fallbacks for slots still
    /// streaming). Render bundles recorded with the materials must be re-recorded
    /// afterwards — call the renderers' <c>MarkStaticBundleDirty</c>.
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <param name="textures">The streamed textures by material texture slot; null
    /// values mean "still streaming" and keep the fallback.</param>
    public void BindTextures(MaterialAsset asset, IReadOnlyDictionary<string, Texture2D?> textures)
    {
        ArgumentNullException.ThrowIfNull(asset);
        Entry entry = GetEntry(asset);
        foreach (KeyValuePair<string, Texture2D?> pair in textures)
        {
            entry.Textures[pair.Key] = pair.Value;
        }

        // The slots were validated against the surface's reflection at compile
        // time; every pass material of the asset carries the surface's full
        // resource set (specialization folds code, not explicit bindings).
        foreach (KeyValuePair<string, GraphicsMaterial> pair in entry.Materials)
        {
            foreach (KeyValuePair<string, Texture2D?> slot in textures)
            {
                string resource = ResourceName(slot.Key);
                pair.Value.SetTexture(resource, slot.Value ?? FallbackFor(resource));
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
        if (entry.ParamsBuffers != null)
        {
            foreach (GraphicsBuffer buffer in entry.ParamsBuffers.Values)
            {
                buffer.Dispose();
            }
        }
    }

    private GraphicsMaterial Compile(MaterialAsset asset, MaterialPassDesc desc, Entry entry)
    {
        Shader shader = ComposeSurfaceShader(asset, desc.TemplateModule, desc.ValueSpecArgs?.Invoke(asset));
        ShaderReflectionInfo reflection = shader.GetShaderModules().ReflectionInfo;

        // Compile-time slot validation: a texture slot the surface does not
        // declare is a typo in the asset — fail here, not at BindTextures.
        IReadOnlyList<string> textureSlots = MaterialComposer.EnumerateTextureSlots(reflection, SurfaceResourceSet);
        foreach (string slot in asset.Textures.Keys)
        {
            if (!textureSlots.Contains(ResourceName(slot)))
            {
                throw new InvalidDataException(
                    $"Material '{asset.Name}' texture slot '{slot}' matches no texture of surface '{SurfaceModuleName(asset)}'; " +
                    $"expected one of: {string.Join(", ", textureSlots.Select(name => name[1..]))}.");
            }
        }

        GraphicsMaterial material = desc.CreateMaterial(asset, shader);

        // The parameter blocks, packed once per asset and shared by its pass
        // materials; each block is bound where the pass's reflection keeps it
        // (a pass that never samples the block's consumers strips it from its
        // layout).
        foreach (KeyValuePair<string, GraphicsBuffer> block in GetParamsBuffers(asset, entry))
        {
            if (reflection.TryGetResourceId(block.Key, out _))
            {
                material.SetBuffer(block.Key, block.Value);
            }
        }

        // Fallback-bind every surface texture slot (streamed values that arrived
        // earlier win); specialization folds keep the full surface resource set
        // in the layout, so the binding side always sees every slot.
        foreach (string resource in textureSlots)
        {
            if (!entry.Textures.TryGetValue(resource[1..], out Texture2D? texture) || texture == null)
            {
                texture = FallbackFor(resource);
            }
            material.SetTexture(resource, texture);
        }
        return material;
    }

    /// <summary>
    /// The parameter buffers of an asset: every block of its surface marked
    /// <c>[MaterialParams]</c> (free names, any number), packed from
    /// <see cref="MaterialAsset.Parameters"/> by member name at the offsets slang
    /// reflected. Created on first compile, shared by every pass material of the
    /// asset.
    /// </summary>
    private IReadOnlyDictionary<string, GraphicsBuffer> GetParamsBuffers(MaterialAsset asset, Entry entry)
    {
        if (entry.ParamsBuffers != null)
        {
            return entry.ParamsBuffers;
        }

        IReadOnlyDictionary<string, IReadOnlyList<SlangUniformMember>> layouts = asset.SurfaceShader == null
            ? new Dictionary<string, IReadOnlyList<SlangUniformMember>>() // the built-in surface marks no blocks
            : Composer.GetParamsLayouts(SurfaceModuleName(asset), defines: asset.Defines);
        if (layouts.Count == 0)
        {
            if (asset.Parameters.Count > 0)
            {
                throw new InvalidDataException(
                    $"Material '{asset.Name}' has parameters, but its surface '{asset.SurfaceShader ?? "pbr-standard (built-in)"}' " +
                    $"declares no [{MaterialComposer.ParamsMarkerAttribute}] parameter block.");
            }
            entry.ParamsBuffers = new Dictionary<string, GraphicsBuffer>();
            return entry.ParamsBuffers;
        }

        entry.ParamsBuffers = Composer.PackParamsBuffers(layouts, asset.Parameters, asset.Name);
        return entry.ParamsBuffers;
    }

    /// <summary>The shader resource name a material texture slot binds to: the slot name with a leading underscore.</summary>
    private static string ResourceName(string slot) => "_" + slot;

    /// <summary>
    /// The fallback texture of one surface texture slot: flat normal for normal maps
    /// (decodes to the identity tangent-space normal), black for emissive (keeps
    /// unstreamed emissive maps dark), white otherwise.
    /// </summary>
    private Texture2D FallbackFor(string resource)
    {
        if (resource.StartsWith("_normal", StringComparison.OrdinalIgnoreCase))
        {
            return FlatNormalTexture;
        }
        if (resource.StartsWith("_emissive", StringComparison.OrdinalIgnoreCase))
        {
            return _rendering.TextureBlack;
        }
        return _rendering.TextureWhite;
    }

    private Texture2D CreateFlatNormalTexture()
    {
        byte[] data = [128, 128, 255, 255];
        return _rendering.CreateTexture2D(data, 1, 1,
            new ImageLoadOption(format: PixelFormat.RGBA8Unorm, addressMode: AddressMode.Repeat, filterMode: FilterMode.Linear, name: "material_flat_normal"));
    }

    private Entry GetEntry(MaterialAsset asset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!_entries.TryGetValue(asset, out Entry? entry))
        {
            entry = new Entry { Asset = asset };
            _entries.Add(asset, entry);
        }
        return entry;
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
                if (entry.ParamsBuffers != null)
                {
                    foreach (GraphicsBuffer buffer in entry.ParamsBuffers.Values)
                    {
                        buffer.Dispose();
                    }
                }
            }
            _entries.Clear();
            Composer.Dispose();
        }
    }
}
