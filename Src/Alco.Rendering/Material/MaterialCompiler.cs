using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

/// <summary>
/// Compiles data-only <see cref="MaterialAsset"/>s into per-pass GPU materials: a
/// passive registry and cache between assets and material passes, pipeline-agnostic.
/// Passes register as <see cref="IMaterialPass"/> implementations where their
/// renderers/features come up (a 2D pipeline's sprite pass, a deferred pipeline's
/// G-buffer/shadow/glass passes, a feature pass like a voxel GI's RSM where the
/// feature is enabled, game-defined passes anywhere), and each (asset, pass) pair
/// compiles lazily on first request, so meshes sharing a material share its GPU
/// materials too.
/// <br/>Every surface is a <see cref="ShaderLibrary"/> exporting the pipeline family's
/// surface contract (e.g. <c>public struct Surface : ISurface</c>). Compilation is
/// slang's own component system: the pass template owns the surface-generic
/// <c>[shader]</c> entry points and composes with the surface module directly
/// (composite + link-time specialization, no generated wrapper modules, no preprocessor
/// stitching) — see <see cref="MaterialComposer"/>, the composition core this class
/// builds its asset policy on. Value specialization replaces pass-private defines (a
/// shadow pass's alpha test is the template's <c>let AlphaTest : bool</c> parameter,
/// fed from the pass's <see cref="IMaterialPass.GetValueSpecArgs"/>).
/// <br/>The parameter mapping reads slang's module-level reflection (a surface's
/// <c>[MaterialParams]</c>-marked blocks may mix scalar and vector float members,
/// under any block names); texture slots are validated against the composed
/// reflection at compile time and bound by name — the asset's own bindings first,
/// streamed overrides (<see cref="BindTextures"/>) second, the asset's fallback policy
/// (<see cref="MaterialAsset.GetTextureFallback"/>) for slots with neither.
/// <br/>Dispose the compiler to release every compiled material and composed shader;
/// use <see cref="Invalidate"/> when an asset file was hot-reloaded into a new
/// instance.
/// </summary>
public sealed class MaterialCompiler : AutoDisposable
{
    /// <summary>The descriptor set index the surface contract reserves for surface resources
    /// (the material frequency group, <c>ALCO_GROUP_MATERIAL</c>).</summary>
    public const int SurfaceResourceSet = 2;

    /// <summary>Compiled materials, streamed-texture slots and the parameter buffers of one material asset.</summary>
    private sealed class Entry
    {
        public Dictionary<string, GraphicsMaterial> Materials { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Texture2D?> Textures { get; } = new(StringComparer.Ordinal);
        public IReadOnlyDictionary<string, GraphicsBuffer>? ParamsBuffers { get; set; }
    }

    private readonly RenderingSystem _rendering;
    private readonly ShaderLibrary? _defaultSurface;
    private readonly Dictionary<string, IMaterialPass> _passes = new(StringComparer.Ordinal);
    private readonly Dictionary<MaterialAsset, Entry> _entries = new();

    /// <summary>
    /// Create the compiler. It starts out knowing no passes; register them as their
    /// renderers/features come up (<see cref="RegisterPass"/>).
    /// </summary>
    /// <param name="rendering">The rendering system (material factory, fallback textures, shared ShaderSystem).</param>
    /// <param name="defaultSurface">
    /// The pipeline family's default surface library, composed when a material names no
    /// <see cref="MaterialAsset.Surface"/> (e.g. World3D's PbrStandard); null requires
    /// every material to name its surface.
    /// </param>
    public MaterialCompiler(RenderingSystem rendering, ShaderLibrary? defaultSurface = null)
    {
        _rendering = rendering;
        _defaultSurface = defaultSurface;
        Composer = new MaterialComposer(rendering);
    }

    /// <summary>
    /// The pipeline-agnostic composition core (template×surface shaders, parameter
    /// layouts, parameter packing). Facilities composing outside the graphics pass
    /// registry — e.g. a voxel GI's compute feed — use it directly.
    /// </summary>
    public MaterialComposer Composer { get; }

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
    /// Whether a pass is registered and accepts the asset — the pass-participation
    /// routing that replaces game-side special cases.
    /// </summary>
    public bool Accepts(MaterialAsset asset, string passId)
    {
        ArgumentNullException.ThrowIfNull(asset);
        return _passes.TryGetValue(passId, out IMaterialPass? pass)
            && pass.Accepts(asset);
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
        if (!_passes.TryGetValue(passId, out IMaterialPass? pass))
        {
            throw new ArgumentException($"No material pass '{passId}' is registered.", nameof(passId));
        }
        if (!pass.Accepts(asset))
        {
            throw new InvalidDataException(
                $"Material pass '{passId}' does not accept material '{asset.Name}'.");
        }

        Entry entry = GetEntry(asset);
        if (!entry.Materials.TryGetValue(passId, out GraphicsMaterial? material))
        {
            material = Compile(asset, pass, entry);
            entry.Materials.Add(passId, material);
        }
        return material;
    }

    /// <summary>
    /// The compiled material of an asset for the pass registered under an id, or null
    /// when no such pass exists or the pass does not accept the asset — e.g. the
    /// optional pass of a feature that is disabled this run, a transparency pass for
    /// an opaque material, or a pass of a different pipeline family.
    /// </summary>
    public GraphicsMaterial? TryGet(MaterialAsset asset, string passId)
        => Accepts(asset, passId) ? Get(asset, passId) : null;

    /// <summary>
    /// The shader of one pass template composed with an asset's surface (the compiler's
    /// default surface when <paramref name="asset"/> is null or names none): what
    /// renderer constructors receive as their pipeline-level default shader. Custom
    /// passes go through <see cref="Composer"/> for non-graphics stage mixes.
    /// </summary>
    /// <param name="asset">The material asset whose surface composes; null selects the default surface.</param>
    /// <param name="template">The pass-template library.</param>
    /// <param name="valueSpecArgs">Value specialization arguments in entry order.</param>
    public Shader ComposeSurfaceShader(
        MaterialAsset? asset, ShaderLibrary template, IReadOnlyList<string>? valueSpecArgs = null)
        => Composer.ComposeGraphics(
            template, SurfaceOf(asset), valueSpecArgs, defines: asset?.Defines);

    /// <summary>
    /// The compute counterpart of <see cref="ComposeSurfaceShader"/>, for facilities
    /// whose surface feed is a compute pass (e.g. a voxel GI's voxelization).
    /// </summary>
    /// <param name="asset">The material asset whose surface composes; null selects the default surface.</param>
    /// <param name="template">The pass-template library.</param>
    /// <param name="valueSpecArgs">Value specialization arguments in entry order.</param>
    public Shader ComposeSurfaceComputeShader(
        MaterialAsset? asset, ShaderLibrary template, IReadOnlyList<string>? valueSpecArgs = null)
        => Composer.ComposeCompute(
            template, SurfaceOf(asset), valueSpecArgs, defines: asset?.Defines);

    /// <summary>
    /// The fallback texture of one surface texture resource of an asset — the asset's
    /// own policy (<see cref="MaterialAsset.GetTextureFallback"/>) resolved to a device
    /// texture. Facilities composing surface feeds outside the graphics pass registry
    /// (e.g. the voxel GI) bind through this.
    /// </summary>
    /// <param name="asset">The material asset whose policy resolves.</param>
    /// <param name="resourceName">The shader resource name of the texture slot (a leading underscore is stripped).</param>
    public Texture2D ResolveFallbackTexture(MaterialAsset asset, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(asset);
        string slot = resourceName.StartsWith('_') ? resourceName[1..] : resourceName;
        return asset.GetTextureFallback(slot) switch
        {
            MaterialTextureFallback.Black => _rendering.TextureBlack,
            MaterialTextureFallback.FlatNormal => _rendering.TextureFlatNormal,
            _ => _rendering.TextureWhite,
        };
    }

    /// <summary>
    /// (Re)bind the streamed textures of one asset, by material texture slot (slot
    /// name = shader resource name without the leading underscore): stores them as
    /// the binding-time values for not-yet-compiled passes and rebinds every
    /// already-compiled pass material (with the asset's fallback policy for slots
    /// still streaming). Streamed values override the asset's own texture bindings
    /// (<see cref="MaterialAsset.Textures"/>). Render bundles recorded with the
    /// materials must be re-recorded afterwards — call the renderers'
    /// <c>MarkStaticBundleDirty</c>.
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
                pair.Value.SetTexture(resource, slot.Value ?? ResolveFallbackTexture(asset, resource));
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

    private GraphicsMaterial Compile(MaterialAsset asset, IMaterialPass pass, Entry entry)
    {
        Shader shader = ComposeSurfaceShader(asset, pass.Template, pass.GetValueSpecArgs(asset));
        ShaderReflectionInfo reflection = shader.GetShaderModules().ReflectionInfo;

        // Compile-time slot validation: a texture slot the surface does not
        // declare is a typo in the asset — fail here, not at BindTextures.
        IReadOnlyList<string> textureSlots = MaterialComposer.EnumerateTextureSlots(reflection, SurfaceResourceSet);
        foreach (string slot in asset.Textures.Keys)
        {
            if (!textureSlots.Contains(ResourceName(slot)))
            {
                throw new InvalidDataException(
                    $"Material '{asset.Name}' texture slot '{slot}' matches no texture of surface '{SurfaceOf(asset).Name}'; " +
                    $"expected one of: {string.Join(", ", textureSlots.Select(name => name[1..]))}.");
            }
        }

        GraphicsMaterial material = pass.CreateMaterial(asset, shader);

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

        // Bind every surface texture slot: a streamed override wins, then the
        // asset's own binding, then the fallback; specialization folds keep the
        // full surface resource set in the layout, so the binding side always
        // sees every slot.
        foreach (string resource in textureSlots)
        {
            string slot = resource[1..];
            if (!entry.Textures.TryGetValue(slot, out Texture2D? texture))
            {
                texture = asset.Textures.GetValueOrDefault(slot);
            }
            material.SetTexture(resource, texture ?? ResolveFallbackTexture(asset, resource));
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

        ShaderLibrary surface = SurfaceOf(asset);
        IReadOnlyDictionary<string, IReadOnlyList<SlangUniformMember>> layouts =
            Composer.GetParamsLayouts(surface, defines: asset.Defines);
        if (layouts.Count == 0)
        {
            if (asset.Parameters.Count > 0)
            {
                throw new InvalidDataException(
                    $"Material '{asset.Name}' has parameters, but its surface '{surface.Name}' " +
                    $"declares no [{MaterialComposer.ParamsMarkerAttribute}] parameter block.");
            }
            entry.ParamsBuffers = new Dictionary<string, GraphicsBuffer>();
            return entry.ParamsBuffers;
        }

        entry.ParamsBuffers = Composer.PackParamsBuffers(layouts, asset.Parameters, asset.Name);
        return entry.ParamsBuffers;
    }

    /// <summary>The surface library an asset composes with: its own, or the compiler's default.</summary>
    /// <exception cref="InvalidDataException">The asset names no surface and the compiler has no default.</exception>
    public ShaderLibrary SurfaceOf(MaterialAsset? asset)
    {
        ShaderLibrary? surface = asset?.Surface ?? _defaultSurface;
        if (surface == null)
        {
            throw new InvalidDataException(asset == null
                ? "No surface library named and the compiler has no default surface."
                : $"Material '{asset.Name}' names no surface and the compiler has no default surface.");
        }
        return surface;
    }

    /// <summary>The shader resource name a material texture slot binds to: the slot name with a leading underscore.</summary>
    private static string ResourceName(string slot) => "_" + slot;

    private Entry GetEntry(MaterialAsset asset)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!_entries.TryGetValue(asset, out Entry? entry))
        {
            entry = new Entry();
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
