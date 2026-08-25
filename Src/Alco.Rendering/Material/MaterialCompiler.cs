using Alco.Graphics;
using Alco.ShaderCompiler;

namespace Alco.Rendering;

/// <summary>
/// Compiles data-only <see cref="MaterialAsset"/>s into per-pass GPU materials: a
/// pass registry plus a stateless (asset, pass) factory, pipeline-agnostic.
/// Passes register as <see cref="IMaterialPass"/> implementations where their
/// renderers/features come up (a 2D pipeline's sprite pass, a deferred pipeline's
/// G-buffer/shadow/glass passes, a feature pass like a voxel GI's RSM where the
/// feature is enabled, game-defined passes anywhere).
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
/// reflection at compile time and bound by name from the asset's own bindings
/// (<see cref="MaterialAsset.Textures"/>), with the asset's fallback policy
/// (<see cref="MaterialAsset.GetTextureFallback"/>) for unbound slots.
/// <br/>Ownership follows the engine's resource rule. Compiled materials are
/// caller-owned: every <see cref="Compile"/> call produces a fresh material, and the
/// caller shares it across the meshes using the asset and disposes it with the
/// owning scene/renderer — or simply drops it, since every GPU object finalizes
/// itself. The compiler keeps no per-asset state, so an unloaded asset and the
/// materials compiled from it are reclaimed by the GC with no notification
/// required. Streamed textures need no accommodation here: streaming pre-creates
/// the texture at its final specification and uploads the content in place, so a
/// bound texture object is never replaced. A hot-reloaded asset file is a new
/// <see cref="MaterialAsset"/> instance; its consumers recompile.
/// <br/>Dispose the compiler to release the composed-shader cache
/// (<see cref="Composer"/>); it owns nothing else.
/// </summary>
public sealed class MaterialCompiler : AutoDisposable
{
    /// <summary>The descriptor set index the surface contract reserves for surface resources
    /// (the material frequency group, <c>ALCO_GROUP_MATERIAL</c>).</summary>
    public const int SurfaceResourceSet = 2;

    private readonly RenderingSystem _rendering;
    private readonly ShaderLibrary? _defaultSurface;
    private readonly Dictionary<string, IMaterialPass> _passes = new(StringComparer.Ordinal);

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
    /// Compile the material of an asset for the pass registered under an id. Every
    /// call compiles a fresh material — the caller owns it: share it across the
    /// meshes using the asset, dispose it with the owning scene/renderer, or drop
    /// it for the GC.
    /// </summary>
    /// <param name="asset">The material asset.</param>
    /// <param name="passId">The registered pass identifier.</param>
    /// <returns>The caller-owned material of the (asset, pass) pair.</returns>
    /// <exception cref="ArgumentException">No pass is registered under <paramref name="passId"/>.</exception>
    /// <exception cref="InvalidDataException">The pass does not accept the asset.</exception>
    public GraphicsMaterial Compile(MaterialAsset asset, string passId)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (!_passes.TryGetValue(passId, out IMaterialPass? pass))
        {
            throw new ArgumentException($"No material pass '{passId}' is registered.", nameof(passId));
        }
        if (!pass.Accepts(asset))
        {
            throw new InvalidDataException(
                $"GraphicsMaterial pass '{passId}' does not accept material '{asset.Name}'.");
        }

        Shader shader = ComposeSurfaceShader(asset, pass.Template, pass.GetValueSpecArgs(asset));
        ShaderReflectionInfo reflection = shader.GetShaderModules().ReflectionInfo;

        // Compile-time slot validation: a texture slot the surface does not
        // declare is a typo in the asset — fail here, at compile time.
        IReadOnlyList<string> textureSlots = MaterialComposer.EnumerateTextureSlots(reflection, SurfaceResourceSet);
        foreach (string slot in asset.Textures.Keys)
        {
            if (!textureSlots.Contains(ResourceName(slot)))
            {
                throw new InvalidDataException(
                    $"GraphicsMaterial '{asset.Name}' texture slot '{slot}' matches no texture of surface '{SurfaceOf(asset).Name}'; " +
                    $"expected one of: {string.Join(", ", textureSlots.Select(name => name[1..]))}.");
            }
        }

        GraphicsMaterial material = pass.CreateMaterial(asset, shader);
        try
        {
            // The parameter blocks, packed from the asset's values; each block is
            // bound where the pass's reflection keeps it (a pass that never samples
            // the block's consumers strips it from its layout). Like every bound
            // slot value, the packed buffers are escapable shared references
            // (ShaderParameterSet.TryGetBuffer) — nobody disposes them explicitly;
            // their finalizer reclaims them once nothing references them.
            foreach (KeyValuePair<string, GraphicsBuffer> block in PackParamsBuffers(asset))
            {
                if (reflection.TryGetResourceId(block.Key, out _))
                {
                    material.SetBuffer(block.Key, block.Value);
                }
            }

            // Bind every surface texture slot from the asset's own bindings, with
            // the asset's fallback policy for unbound slots; specialization folds
            // keep the full surface resource set in the layout, so the binding
            // side always sees every slot.
            foreach (string resource in textureSlots)
            {
                string slot = resource[1..];
                Texture2D? texture = asset.Textures.GetValueOrDefault(slot);
                material.SetTexture(resource, texture ?? ResolveFallbackTexture(asset, resource));
            }
        }
        catch
        {
            material.Dispose();
            throw;
        }
        return material;
    }

    /// <summary>
    /// The compiled material of an asset for the pass registered under an id, or null
    /// when no such pass exists or the pass does not accept the asset — e.g. the
    /// optional pass of a feature that is disabled this run, a transparency pass for
    /// an opaque material, or a pass of a different pipeline family.
    /// </summary>
    public GraphicsMaterial? TryCompile(MaterialAsset asset, string passId)
        => Accepts(asset, passId) ? Compile(asset, passId) : null;

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
    /// The compute counterpart of <see cref="Compile"/>: the material of an asset for a
    /// compute pass template (e.g. a voxel GI's voxelization), under the same slot rules
    /// as the graphics passes — texture slots are validated against the composed
    /// reflection and bound from the asset's own bindings (the asset's fallback policy
    /// for unbound slots), and the surface's parameter blocks are packed and bound.
    /// Compute passes have no registry; the template is handed in directly.
    /// <br/>The material is caller-owned: share it across the dispatches using the asset
    /// and drop it with the owning facility; a compute material holds no disposable
    /// state of its own, and the GPU resources it references finalize themselves.
    /// </summary>
    /// <param name="asset">The material asset; its fallback policy covers unbound slots.</param>
    /// <param name="template">The pass-template library.</param>
    /// <param name="valueSpecArgs">Value specialization arguments in entry order.</param>
    /// <returns>The caller-owned compute material, fully bound except facility data.</returns>
    /// <exception cref="InvalidDataException">A texture slot or parameter of the asset matches nothing on the surface.</exception>
    public ComputeMaterial CompileCompute(
        MaterialAsset asset, ShaderLibrary template, IReadOnlyList<string>? valueSpecArgs = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Shader shader = ComposeSurfaceComputeShader(asset, template, valueSpecArgs);
        ShaderReflectionInfo reflection = shader.GetShaderModules().ReflectionInfo;

        // Compile-time slot validation, the same rule as the graphics passes: a
        // texture slot the surface does not declare is a typo in the asset.
        IReadOnlyList<string> textureSlots = MaterialComposer.EnumerateTextureSlots(reflection, SurfaceResourceSet);
        foreach (string slot in asset.Textures.Keys)
        {
            if (!textureSlots.Contains(ResourceName(slot)))
            {
                throw new InvalidDataException(
                    $"Compute material '{asset.Name}' texture slot '{slot}' matches no texture of surface '{SurfaceOf(asset).Name}'; " +
                    $"expected one of: {string.Join(", ", textureSlots.Select(name => name[1..]))}.");
            }
        }

        ComputeMaterial material = _rendering.CreateComputeMaterial(shader);
        foreach (KeyValuePair<string, GraphicsBuffer> block in PackParamsBuffers(asset))
        {
            if (reflection.TryGetResourceId(block.Key, out _))
            {
                material.SetBuffer(block.Key, block.Value);
            }
        }

        // Bind every surface texture slot from the asset's own bindings, with the
        // asset's fallback policy for unbound slots — the bindings are final: streamed
        // textures upload in place and are never replaced.
        foreach (string resource in textureSlots)
        {
            string slot = resource[1..];
            Texture2D? texture = asset.Textures.GetValueOrDefault(slot);
            material.SetTexture(resource, texture ?? ResolveFallbackTexture(asset, resource));
        }
        return material;
    }

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
    /// The parameter buffers of an asset: every block of its surface marked
    /// <c>[MaterialParams]</c> (free names, any number), packed from
    /// <see cref="MaterialAsset.Parameters"/> by member name at the offsets slang
    /// reflected. Packed fresh per compile; the buffers live as bound slot values
    /// and are reclaimed by their finalizer like any other escapable binding.
    /// </summary>
    private IReadOnlyDictionary<string, GraphicsBuffer> PackParamsBuffers(MaterialAsset asset)
    {
        ShaderLibrary surface = SurfaceOf(asset);
        IReadOnlyDictionary<string, IReadOnlyList<SlangUniformMember>> layouts =
            Composer.GetParamsLayouts(surface, defines: asset.Defines);
        if (layouts.Count == 0)
        {
            if (asset.Parameters.Count > 0)
            {
                throw new InvalidDataException(
                    $"GraphicsMaterial '{asset.Name}' has parameters, but its surface '{surface.Name}' " +
                    $"declares no [{MaterialComposer.ParamsMarkerAttribute}] parameter block.");
            }
            return new Dictionary<string, GraphicsBuffer>();
        }
        return Composer.PackParamsBuffers(layouts, asset.Parameters, asset.Name);
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
                : $"GraphicsMaterial '{asset.Name}' names no surface and the compiler has no default surface.");
        }
        return surface;
    }

    /// <summary>The shader resource name a material texture slot binds to: the slot name with a leading underscore.</summary>
    private static string ResourceName(string slot) => "_" + slot;

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Compiled materials are caller-owned; the compiler owns only the
            // composed-shader cache.
            Composer.Dispose();
        }
    }
}
