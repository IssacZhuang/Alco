using Alco;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Compiles data-only <see cref="MaterialAsset"/>s into the per-pass GPU materials of the
/// deferred PBR pipeline: the single place that maps material data onto the pass renderers'
/// factory methods, replacing per-application material setup. One entry exists per asset;
/// each pass material is created lazily on first request and reused afterwards, so meshes
/// sharing a material share its GPU materials too.
/// <br/>Pass-mandated state (depth/blend/rasterizer, internal buffer bindings, fallback
/// textures) stays with the renderers — the compiler owns the mapping, the caching and the
/// lifetime of the created materials (dispose the compiler to release them all; use
/// <see cref="Invalidate"/> when an asset file was hot-reloaded into a new instance).
/// Per-instance data (base color, metallic/roughness, emissive, alpha cutoff) rides the
/// renderers' instance buffers, not the material — renderables read it from the asset.
/// </summary>
public sealed class MaterialCompiler : AutoDisposable
{
    /// <summary>Compiled materials and streamed-texture bindings of one material asset.</summary>
    private sealed class Entry
    {
        public required MaterialAsset Asset { get; init; }
        public GraphicsMaterial? GBuffer { get; set; }
        public GraphicsMaterial? Shadow { get; set; }
        public GraphicsMaterial? Rsm { get; set; }
        public GraphicsMaterial? ForwardGlass { get; set; }
        public Texture2D? Albedo { get; set; }
        public Texture2D? Normal { get; set; }
        public Texture2D? MetallicRoughness { get; set; }
        public Texture2D? Emissive { get; set; }
    }

    private readonly RenderingSystem _rendering;
    private readonly GBufferRenderer _gbuffer;
    private readonly ShadowRenderer? _shadow;
    private readonly RGNode_Forward? _forward;
    private readonly Dictionary<MaterialAsset, Entry> _entries = new();

    /// <summary>
    /// Create the compiler bound to the pass renderers it serves. The shadow and forward
    /// renderers are optional: without them <see cref="GetShadow"/> throws,
    /// <see cref="GetRsm"/> reports null, and no other pass is affected.
    /// </summary>
    /// <param name="rendering">The rendering system (fallback texture source).</param>
    /// <param name="gbuffer">The G-buffer renderer.</param>
    /// <param name="shadow">The shadow renderer, or null when shadows are unused.</param>
    /// <param name="forward">The forward transparency node, or null when unused.</param>
    public MaterialCompiler(
        RenderingSystem rendering,
        GBufferRenderer gbuffer,
        ShadowRenderer? shadow = null,
        RGNode_Forward? forward = null)
    {
        _rendering = rendering;
        _gbuffer = gbuffer;
        _shadow = shadow;
        _forward = forward;
    }

    /// <summary>The G-buffer material of an asset; created on first request, then cached.</summary>
    /// <param name="asset">The material asset.</param>
    /// <returns>The caller-unsafe (compiler-owned) G-buffer material.</returns>
    public GraphicsMaterial GetGBuffer(MaterialAsset asset)
    {
        Entry entry = GetEntry(asset);
        if (entry.GBuffer == null)
        {
            entry.GBuffer = _gbuffer.CreateMaterial(
                entry.Albedo, entry.Normal, entry.MetallicRoughness, entry.Emissive,
                asset.DoubleSided, $"{asset.Name}_gbuffer");
        }
        return entry.GBuffer;
    }

    /// <summary>
    /// The shadow depth material of an asset: alpha-tested (<see cref="MeshAlphaMode.Mask"/>)
    /// assets get the cutout variant that samples the albedo alpha and discards transparent
    /// fragments; everything else gets the plain opaque depth material.
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <returns>The compiler-owned shadow material.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the compiler has no shadow
    /// renderer bound.</exception>
    public GraphicsMaterial GetShadow(MaterialAsset asset)
    {
        if (_shadow == null)
        {
            throw new InvalidOperationException("The material compiler has no shadow renderer bound; pass one to its constructor to compile shadow materials.");
        }

        Entry entry = GetEntry(asset);
        if (entry.Shadow == null)
        {
            entry.Shadow = asset.AlphaMode == MeshAlphaMode.Mask
                ? _shadow.CreateShadowCutoutMaterial(entry.Albedo, asset.DoubleSided, $"{asset.Name}_shadow_cutout")
                : _shadow.CreateShadowMaterial(asset.DoubleSided, $"{asset.Name}_shadow");
        }
        return entry.Shadow;
    }

    /// <summary>
    /// The reflective-shadow-map material of an asset; null while the shadow renderer has
    /// no RSM pass enabled (see <see cref="ShadowRenderer.EnableRsm"/>).
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <returns>The compiler-owned RSM material, or null when RSM is disabled.</returns>
    public GraphicsMaterial? GetRsm(MaterialAsset asset)
    {
        if (_shadow == null || !_shadow.IsRsmEnabled)
        {
            return null;
        }

        Entry entry = GetEntry(asset);
        if (entry.Rsm == null)
        {
            entry.Rsm = _shadow.CreateRsmMaterial(entry.Albedo, asset.DoubleSided, $"{asset.Name}_rsm");
        }
        return entry.Rsm;
    }

    /// <summary>
    /// The forward transparency (glass) material of an asset, for alpha-blended
    /// (<see cref="MeshAlphaMode.Blend"/>) materials.
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <returns>The compiler-owned glass material.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the compiler has no forward
    /// node bound.</exception>
    public GraphicsMaterial GetForwardGlass(MaterialAsset asset)
    {
        if (_forward == null)
        {
            throw new InvalidOperationException("The material compiler has no forward node bound; pass one to its constructor to compile glass materials.");
        }

        Entry entry = GetEntry(asset);
        if (entry.ForwardGlass == null)
        {
            entry.ForwardGlass = _forward.CreateGlassMaterial(
                entry.Albedo, entry.Normal, entry.MetallicRoughness, entry.Emissive,
                asset.DoubleSided, $"{asset.Name}_glass");
        }
        return entry.ForwardGlass;
    }

    /// <summary>
    /// (Re)bind the streamed textures of one asset: stores them as the creation-time
    /// bindings for not-yet-compiled passes and rebinds every already-compiled pass
    /// material (with the renderers' fallback textures for slots still streaming). Render
    /// bundles recorded with the materials must be re-recorded afterwards — call the
    /// renderers' <c>MarkStaticBundleDirty</c>.
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <param name="albedoTexture">The streamed albedo texture, or null.</param>
    /// <param name="normalTexture">The streamed normal map, or null.</param>
    /// <param name="metallicRoughnessTexture">The streamed metallic-roughness texture, or null.</param>
    /// <param name="emissiveTexture">The streamed emissive texture, or null.</param>
    public void BindTextures(
        MaterialAsset asset,
        Texture2D? albedoTexture,
        Texture2D? normalTexture,
        Texture2D? metallicRoughnessTexture,
        Texture2D? emissiveTexture)
    {
        Entry entry = GetEntry(asset);
        entry.Albedo = albedoTexture;
        entry.Normal = normalTexture;
        entry.MetallicRoughness = metallicRoughnessTexture;
        entry.Emissive = emissiveTexture;

        if (entry.GBuffer != null)
        {
            _gbuffer.SetMaterialTextures(entry.GBuffer, albedoTexture, normalTexture, metallicRoughnessTexture, emissiveTexture);
        }
        if (entry.Shadow != null && asset.AlphaMode == MeshAlphaMode.Mask)
        {
            _shadow!.SetShadowCutoutMaterialTextures(entry.Shadow, albedoTexture);
        }
        if (entry.Rsm != null)
        {
            // The RSM material has no dedicated rebind API on the shadow renderer;
            // its only slot mirrors the cutout binding: the albedo with white fallback.
            entry.Rsm.SetTexture("_albedoTexture", albedoTexture ?? _rendering.TextureWhite);
        }
        if (entry.ForwardGlass != null)
        {
            _forward!.SetGlassMaterialTextures(entry.ForwardGlass, albedoTexture, normalTexture, metallicRoughnessTexture, emissiveTexture);
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
            DisposeEntryMaterials(entry);
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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (KeyValuePair<MaterialAsset, Entry> pair in _entries)
            {
                DisposeEntryMaterials(pair.Value);
            }
            _entries.Clear();
        }
    }

    private static void DisposeEntryMaterials(Entry entry)
    {
        entry.GBuffer?.Dispose();
        entry.Shadow?.Dispose();
        entry.Rsm?.Dispose();
        entry.ForwardGlass?.Dispose();
        entry.GBuffer = null;
        entry.Shadow = null;
        entry.Rsm = null;
        entry.ForwardGlass = null;
    }
}
