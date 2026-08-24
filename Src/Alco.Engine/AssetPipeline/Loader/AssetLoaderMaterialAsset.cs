using Alco.IO;
using Alco.Rendering;

namespace Alco.Engine;

/// <summary>
/// Loads material asset files (<c>.amat</c>) into data-only <see cref="MaterialAsset"/>s.
/// Parsing touches no GPU resources and never loads the referenced textures — those stay
/// paths on the asset for streaming warm-up. The file's <c>type</c> discriminator selects
/// the pipeline family's schema (<see cref="MaterialAssetJson.RegisterType{TJson}"/>);
/// a file without one parses as the pipeline-agnostic base schema.
/// </summary>
public class AssetLoaderMaterialAsset : BaseAssetLoader<MaterialAsset>
{
    /// <inheritdoc />
    public override string Name => "MaterialAsset(.amat)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [FileExt.Material];

    /// <inheritdoc />
    public override object CreateAsset(in AssetLoadContext context)
    {
        return MaterialAssetJson.Parse(context.GetData(), context.Filename);
    }
}
