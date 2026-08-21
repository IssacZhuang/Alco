using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// One pass's policy for compiling <see cref="MaterialAsset"/>s into GPU materials: how
/// the pass's material is created (the pass template composed with the asset's surface,
/// plus the pass-mandated state) and how streamed textures rebind into it. Instances are
/// registered on <see cref="MaterialCompiler.RegisterPass"/> by the party that owns the
/// pass — the renderer itself for the standard passes, the enabling feature for
/// feature-specific ones (e.g. the voxel GI registers the RSM pass where it calls
/// <c>ShadowRenderer.EnableRsm</c>); nothing outside the pass knows about it.
/// </summary>
public interface IMaterialPass
{
    /// <summary>The unique material-pass identifier ("gbuffer", "shadow", "rsm", "glass").</summary>
    string Id { get; }

    /// <summary>
    /// Create the pass's GPU material for one asset. Pass-mandated state (depth/blend/
    /// rasterizer, internal buffer bindings, standard-slot fallback textures) belongs to
    /// the implementation; the material is owned by the compiler afterwards.
    /// </summary>
    /// <param name="context">The compile context of this (asset, pass) pair.</param>
    /// <returns>The compiled material.</returns>
    GraphicsMaterial Compile(MaterialCompileContext context);

    /// <summary>
    /// (Re)bind the streamed textures of one asset into an already-compiled material,
    /// applying the pass's fallback textures for slots still streaming. Called by
    /// <see cref="MaterialCompiler.BindTextures"/>; render bundles recorded with the
    /// material must be re-recorded afterwards (the renderers' MarkStaticBundleDirty).
    /// </summary>
    /// <param name="context">The compile context of this (asset, pass) pair.</param>
    /// <param name="material">A material this pass compiled earlier.</param>
    /// <param name="slots">The material texture slots to bind, by slot name; null values
    /// mean "still streaming" and take the pass's fallback.</param>
    void RebindTextures(MaterialCompileContext context, GraphicsMaterial material, IReadOnlyDictionary<string, Texture2D?> slots);
}

/// <summary>
/// What <see cref="IMaterialPass.Compile"/> and <see cref="IMaterialPass.RebindTextures"/>
/// see: the asset being compiled, the rendering system, and the shader composer that
/// produces a pass template's shader with the asset's surface swapped in (the built-in
/// PbrStandard surface when the asset names none).
/// </summary>
public sealed class MaterialCompileContext
{
    /// <summary>The material asset being compiled.</summary>
    public required MaterialAsset Asset { get; init; }

    /// <summary>The rendering system (fallback texture source, material factory).</summary>
    public required RenderingSystem Rendering { get; init; }

    /// <summary>
    /// Pass-template asset path → the compiled shader for this asset's surface: the
    /// template as shipped when the asset uses the built-in surface, the template with
    /// the asset's surface spliced into its <c>@SURFACE@</c> line otherwise.
    /// </summary>
    public required Func<string, Shader> ComposeShader { get; init; }
}
