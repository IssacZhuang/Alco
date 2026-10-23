using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Entry point of the World3D asset pipeline: the file extensions it owns and one-call registration of its asset loaders.
/// </summary>
public static class World3DAssetPipeline
{
    /// <summary>Extension of mesh asset packages (<c>.amsh</c>).</summary>
    public const string MeshExtension = ".amsh";

    /// <summary>Extension of model asset files (<c>.amdl</c>).</summary>
    public const string ModelExtension = ".amdl";

    /// <summary>
    /// Registers the World3D asset loaders; call once after engine startup and before loading any World3D assets.
    /// </summary>
    /// <param name="assetSystem">The asset system to register with.</param>
    /// <param name="renderingSystem">The rendering system, or null for header-only mesh assets.</param>
    public static void RegisterLoaders(AssetSystem assetSystem, RenderingSystem? renderingSystem = null)
    {
        assetSystem.RegisterAssetLoader(new AssetLoaderMeshAsset(renderingSystem));
        assetSystem.RegisterAssetLoader(new AssetLoaderModelAsset());
    }

    /// <summary>
    /// Create the material compiler of the World3D pipeline family: the engine's
    /// <see cref="MaterialCompiler"/> with the built-in PbrStandard surface as the
    /// default surface.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    public static MaterialCompiler CreateMaterialCompiler(RenderingSystem rendering)
        => new(rendering, rendering.ShaderSystem.GetLibrary(PbrMaterialAsset.DefaultSurfaceModule));
}
