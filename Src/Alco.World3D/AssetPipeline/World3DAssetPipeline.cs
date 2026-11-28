using Alco.IO;

namespace Alco.World3D;

/// <summary>
/// Entry point of the World3D asset pipeline: the file extensions it owns and registration of its asset loaders.
/// </summary>
public static class World3DAssetPipeline
{
    /// <summary>Extension of mesh asset packages (<c>.amsh</c>).</summary>
    public const string MeshExtension = ".amsh";

    /// <summary>Extension of model asset files (<c>.amdl</c>).</summary>
    public const string ModelExtension = ".amdl";

    /// <summary>
    /// Registers the World3D asset loaders and ensures World3D material types are loaded for polymorphic JSON parsing.
    /// </summary>
    /// <param name="assetSystem">The asset system to register with.</param>
    public static void RegisterLoaders(AssetSystem assetSystem)
    {
        ArgumentNullException.ThrowIfNull(assetSystem);
        _ = typeof(PbrMaterialAsset);
        assetSystem.RegisterAssetLoader(new AssetLoaderMeshAsset());
        assetSystem.RegisterAssetLoader(new AssetLoaderModelAsset());
    }
}
