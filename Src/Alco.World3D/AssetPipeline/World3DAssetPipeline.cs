using Alco.IO;
using Alco.Rendering;

namespace Alco.World3D;

/// <summary>
/// Entry point of the World3D asset pipeline: the file extensions this module owns and a
/// one-call registration of its asset loaders. The engine core does not reference
/// Alco.World3D — applications wire this module in themselves (engine startup, then this call).
/// </summary>
public static class World3DAssetPipeline
{
    /// <summary>Extension of cooked mesh packages (<c>.amsh</c>).</summary>
    public const string MeshExtension = ".amsh";

    /// <summary>
    /// Register the module's asset loaders with the engine's asset system. Call once after
    /// engine startup, before loading any World3D assets. A bound rendering system enables GPU
    /// residency (<see cref="MeshStream.LoadLodAsync"/>); without one, mesh assets load
    /// header-only.
    /// </summary>
    /// <param name="assetSystem">The asset system to register with.</param>
    /// <param name="renderingSystem">The rendering system, or null for header-only assets.</param>
    public static void RegisterLoaders(AssetSystem assetSystem, RenderingSystem? renderingSystem = null)
    {
        assetSystem.RegisterAssetLoader(new AssetLoaderMeshStream(renderingSystem));
    }
}
