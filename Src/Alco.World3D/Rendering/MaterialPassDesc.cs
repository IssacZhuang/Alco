using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// The registration record of one material pass — everything the
/// <see cref="MaterialCompiler"/> needs to compile <see cref="MaterialAsset"/>s into
/// the pass's GPU materials, with no pass-specific code in the compiler itself.
/// A pass pairs a pass-template slang module (owning the surface-generic
/// <c>[shader]</c> entry points, e.g. <c>gbuffer</c>) with a material factory
/// carrying the pass-mandated pipeline state (depth/blend/rasterizer, internal
/// buffer bindings). Passes are registered where their renderer or feature comes
/// up; games and custom pipelines register their own passes the same way, without
/// touching the engine.
/// </summary>
public sealed record MaterialPassDesc
{
    /// <summary>The unique material-pass identifier ("gbuffer", "shadow", "rsm", "glass").</summary>
    public required string Id { get; init; }

    /// <summary>The pass-template slang module name (composes with each material's surface module).</summary>
    public required string TemplateModule { get; init; }

    /// <summary>
    /// Create the pass's GPU material for one asset from the composed template shader:
    /// applies the pass-mandated state. The compiler owns the material afterwards.
    /// </summary>
    public required Func<MaterialAsset, Shader, GraphicsMaterial> CreateMaterial { get; init; }

    /// <summary>
    /// Value specialization arguments of the template's entries, derived from the asset
    /// (e.g. the shadow pass maps <see cref="MaterialAsset.AlphaMode"/> to its
    /// <c>let AlphaTest : bool</c> parameter). Null when the template takes none.
    /// </summary>
    public Func<MaterialAsset, IReadOnlyList<string>>? ValueSpecArgs { get; init; }

    /// <summary>
    /// Whether the pass participates for one asset (e.g. the glass pass only accepts
    /// <see cref="MeshAlphaMode.Blend"/> materials). Null accepts everything;
    /// <see cref="MaterialCompiler.TryGet"/> returns null for rejected assets.
    /// </summary>
    public Func<MaterialAsset, bool>? Accepts { get; init; }
}
