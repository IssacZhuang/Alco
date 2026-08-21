using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Loads cooked mesh packages (.amsh) into <see cref="MeshStream"/> handles. Only the meta
/// and tables are parsed at load time — the returned asset holds no geometry and streams LOD
/// payloads on demand. Falls back to an in-memory open when the asset context has preloaded
/// data (e.g. TryDecode) instead of a stream. Registered through
/// <see cref="World3DAssetPipeline.RegisterLoaders"/> — the engine core does not know this module.
/// </summary>
public sealed class AssetLoaderMeshStream : BaseAssetLoader<MeshStream>
{
    private readonly RenderingSystem? _renderingSystem;

    /// <summary>
    /// Creates the loader. A rendering system binds a GPU device to the created assets so
    /// <see cref="MeshStream.LoadLodAsync"/> works; without one the assets are header-only.
    /// </summary>
    /// <param name="renderingSystem">The rendering system, or null for header-only assets.</param>
    public AssetLoaderMeshStream(RenderingSystem? renderingSystem = null)
    {
        _renderingSystem = renderingSystem;
    }

    /// <inheritdoc />
    public override string Name => "MeshStream(.amsh)";

    /// <inheritdoc />
    public override IReadOnlyList<string> FileExtensions => [World3DAssetPipeline.MeshExtension];

    /// <inheritdoc />
    public override object CreateAsset(in AssetLoadContext context)
    {
        // The stream is owned by the asset from here on; do not dispose it in this method.
        return context.CanGetStream
            ? MeshStream.FromStream(context.GetStream(), _renderingSystem?.GraphicsDevice)
            : MeshStream.FromMemory(context.GetData(), _renderingSystem?.GraphicsDevice);
    }
}
