using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Entry point of the World3D asset pipeline: the file extensions this module owns and a
/// one-call registration of its asset loaders. The engine core does not reference
/// Alco.World3D — applications wire this module in themselves (engine startup, then this call).
/// <br/>The 3D asset chain: <c>.amsh</c> meshes expose named material slots (never
/// materials), <c>.amat</c> files are data-only material descriptions (loaded by the
/// engine's <c>AssetLoaderMaterialAsset</c>; this call registers the <c>"pbr"</c> type
/// discriminator selecting <see cref="PbrMaterialAsset"/>), and <c>.amdl</c> model assets
/// are the composition layer binding materials to mesh slots.
/// </summary>
public static class World3DAssetPipeline
{
    /// <summary>Extension of mesh asset packages (<c>.amsh</c>).</summary>
    public const string MeshExtension = ".amsh";

    /// <summary>Extension of model asset files (<c>.amdl</c>).</summary>
    public const string ModelExtension = ".amdl";

    /// <summary>
    /// Register the module's asset loaders with the engine's asset system. Call once after
    /// engine startup, before loading any World3D assets. A bound rendering system enables GPU
    /// residency (<see cref="MeshAsset.LoadLodAsync"/>); without one, mesh assets load
    /// header-only. Material and model assets are pure data and never need a device.
    /// </summary>
    /// <param name="assetSystem">The asset system to register with.</param>
    /// <param name="renderingSystem">The rendering system, or null for header-only mesh assets.</param>
    public static void RegisterLoaders(AssetSystem assetSystem, RenderingSystem? renderingSystem = null)
    {
        assetSystem.RegisterAssetLoader(new AssetLoaderMeshAsset(renderingSystem));
        assetSystem.RegisterAssetLoader(new AssetLoaderModelAsset());
        MaterialAssetJson.RegisterType<PbrMaterialAssetJson>("pbr");
    }

    /// <summary>
    /// Create the material compiler of the World3D pipeline family: the engine's
    /// <see cref="MaterialCompiler"/> with the built-in PbrStandard surface as the
    /// default surface.
    /// </summary>
    /// <param name="rendering">The rendering system used to create GPU resources.</param>
    public static MaterialCompiler CreateMaterialCompiler(RenderingSystem rendering)
        => new(rendering, PbrMaterialAsset.DefaultSurfacePath);
}
