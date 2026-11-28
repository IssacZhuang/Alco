using Alco.IO;

namespace Alco.World3D;

/// <summary>
/// Loads mesh asset packages (<c>.amsh</c>) into pure CPU/IO <see cref="MeshAsset"/> handles whose
/// LOD payloads stream on demand without holding GPU dependencies.
/// </summary>
public sealed class AssetLoaderMeshAsset : BaseAssetLoader<MeshAsset>
{
    /// <inheritdoc />
    public override string Name => "MeshAsset(.amsh)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [World3DAssetPipeline.MeshExtension];

    /// <inheritdoc />
    public override object CreateAsset(in AssetLoadContext context)
    {
        return context.CanGetStream
            ? MeshAsset.FromStream(context.GetStream())
            : MeshAsset.FromMemory(context.GetData());
    }
}
