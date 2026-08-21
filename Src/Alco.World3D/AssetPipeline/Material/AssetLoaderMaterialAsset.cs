using Alco.IO;

namespace Alco.World3D;

/// <summary>
/// Loads material asset files (<c>.amat</c>) into data-only <see cref="MaterialAsset"/>s.
/// Parsing touches no GPU resources and never loads the referenced textures — those stay
/// paths on the asset for streaming warm-up. Registered through
/// <see cref="World3DAssetPipeline.RegisterLoaders"/>.
/// </summary>
public sealed class AssetLoaderMaterialAsset : BaseAssetLoader<MaterialAsset>
{
    /// <inheritdoc />
    public override string Name => "MaterialAsset(.amat)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [World3DAssetPipeline.MaterialExtension];

    /// <inheritdoc />
    public override object CreateAsset(in AssetLoadContext context)
    {
        return MaterialAssetJson.Parse(context.GetData(), context.Filename);
    }
}
