using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// One material pass of a rendering facility — everything the <see cref="MaterialCompiler"/>
/// needs to compile <see cref="MaterialAsset"/>s into the pass's GPU materials, with no
/// pass-specific code in the compiler itself. A pass pairs a pass-template slang module
/// (owning the surface-generic <c>[shader]</c> entry points) with a material factory
/// applying the pass-mandated pipeline state (depth/blend/rasterizer, internal buffer
/// bindings). Passes are implemented where their renderer or feature comes up; games and
/// custom pipelines register their own passes the same way, without touching the engine.
/// <br/>Implement <see cref="IMaterialPass{TAsset}"/> to receive the pipeline family's
/// asset type statically.
/// </summary>
public interface IMaterialPass
{
    /// <summary>The unique material-pass identifier ("gbuffer", "shadow", ...), unique within its compiler.</summary>
    string Id { get; }

    /// <summary>The pass-template shader library (composes with each material's surface library).</summary>
    ShaderLibrary Template { get; }

    /// <summary>
    /// Create the pass's GPU material for one asset from the composed template shader:
    /// applies the pass-mandated state. Ownership transfers to the compile caller
    /// (see <see cref="MaterialCompiler.Compile"/>).
    /// </summary>
    GraphicsMaterial CreateMaterial(MaterialAsset asset, Shader shader);

    /// <summary>
    /// Value specialization arguments of the template's entries, derived from the asset
    /// (e.g. a shadow pass mapping the asset's alpha handling to its
    /// <c>let AlphaTest : bool</c> parameter). Null/default when the template takes none.
    /// </summary>
    IReadOnlyList<string>? GetValueSpecArgs(MaterialAsset asset) => null;

    /// <summary>
    /// Whether the pass participates for one asset (e.g. a transparency pass only accepts
    /// blend materials). The default accepts everything;
    /// <see cref="MaterialCompiler.TryCompile"/> returns null for rejected assets.
    /// </summary>
    bool Accepts(MaterialAsset asset) => true;
}

/// <summary>
/// <see cref="IMaterialPass"/> typed to a pipeline family's asset class. The base
/// interface's members forward with a checked cast: a foreign-family asset is not
/// accepted, and can never reach <see cref="CreateMaterial(TAsset, Shader)"/> through
/// the compiler.
/// </summary>
/// <typeparam name="TAsset">The material asset type this pass consumes.</typeparam>
public interface IMaterialPass<TAsset> : IMaterialPass where TAsset : MaterialAsset
{
    /// <inheritdoc cref="IMaterialPass.CreateMaterial"/>
    GraphicsMaterial CreateMaterial(TAsset asset, Shader shader);

    /// <inheritdoc cref="IMaterialPass.GetValueSpecArgs"/>
    IReadOnlyList<string>? GetValueSpecArgs(TAsset asset) => null;

    /// <inheritdoc cref="IMaterialPass.Accepts"/>
    bool Accepts(TAsset asset) => true;

    GraphicsMaterial IMaterialPass.CreateMaterial(MaterialAsset asset, Shader shader)
        => CreateMaterial(Require(asset), shader);

    IReadOnlyList<string>? IMaterialPass.GetValueSpecArgs(MaterialAsset asset)
        => GetValueSpecArgs(Require(asset));

    bool IMaterialPass.Accepts(MaterialAsset asset)
        => asset is TAsset typed && Accepts(typed);

    private static TAsset Require(MaterialAsset asset) => asset is TAsset typed
        ? typed
        : throw new InvalidDataException(
            $"Material '{asset.Name}' is a {asset.GetType().Name}; this pass requires a {typeof(TAsset).Name}.");
}
