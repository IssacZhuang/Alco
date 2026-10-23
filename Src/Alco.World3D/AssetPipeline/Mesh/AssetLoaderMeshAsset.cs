using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Loads mesh asset packages (<c>.amsh</c>) into header-only <see cref="MeshAsset"/> handles whose LOD payloads stream on demand.
/// </summary>
public sealed class AssetLoaderMeshAsset : BaseAssetLoader<MeshAsset>
{
    private readonly RenderingSystem? _renderingSystem;

    /// <summary>
    /// Creates the loader. A rendering system binds a GPU device to the created assets so
    /// <see cref="MeshAsset.LoadLodAsync"/> works; without one the assets are header-only.
    /// </summary>
    /// <param name="renderingSystem">The rendering system, or null for header-only assets.</param>
    public AssetLoaderMeshAsset(RenderingSystem? renderingSystem = null)
    {
        _renderingSystem = renderingSystem;
    }

    /// <inheritdoc />
    public override string Name => "MeshAsset(.amsh)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [World3DAssetPipeline.MeshExtension];

    /// <inheritdoc />
    public override object CreateAsset(in AssetLoadContext context)
    {
        // The stream is owned by the asset from here on; do not dispose it in this method.
        return context.CanGetStream
            ? MeshAsset.FromStream(context.GetStream(), _renderingSystem?.GraphicsDevice)
            : MeshAsset.FromMemory(context.GetData(), _renderingSystem?.GraphicsDevice);
    }
}
