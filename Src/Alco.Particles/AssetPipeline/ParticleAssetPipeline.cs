using Alco.IO;
using Alco.Rendering;

namespace Alco.Particles;

/// <summary>
/// The module entry point of the Alco.Particles asset pipeline: registers the
/// particle effect asset loader (<c>.afx</c>) with an asset system. Shader
/// modules of the module resolve by name once the module's <c>Assets</c> folder is
/// reachable by the asset system (the module csproj ships it next to the
/// consuming app; dev-time hot reload mounts it as a file source).
/// </summary>
public static class ParticleAssetPipeline
{
    /// <summary>The file extension of particle effect assets.</summary>
    public const string EffectExtension = ".afx";

    /// <summary>The module name of the built-in 2D particle render pass template.</summary>
    public const string RenderModule2D = "GpuParticle2D";

    /// <summary>The module name of the built-in 3D particle render pass template.</summary>
    public const string RenderModule3D = "GpuParticle3D";

    /// <summary>
    /// The module name of the default particle surface (texture × particle color),
    /// composed with the render pass templates for groups whose material names no
    /// surface module of its own.
    /// </summary>
    public const string DefaultSurface = "AlcoParticles-SurfaceDefault";

    /// <summary>
    /// The module name of the default 2D simulation behavior (used by 2D groups
    /// without an explicit <see cref="ParticleGroupAsset.Behavior"/>).
    /// </summary>
    public const string DefaultBehavior2D = "AlcoParticles-Default2D";

    /// <summary>
    /// The module name of the default 3D simulation behavior (used by 3D groups
    /// without an explicit <see cref="ParticleGroupAsset.Behavior"/>).
    /// </summary>
    public const string DefaultBehavior3D = "AlcoParticles-Default3D";

    /// <summary>
    /// Registers the particle effect asset loader with the asset system.
    /// </summary>
    /// <param name="assetSystem">The asset system to register the loader with.</param>
    /// <param name="renderingSystem">
    /// The rendering system whose shader system resolves behavior/shader module
    /// references at load time; null defers module resolution to instance creation
    /// (module references stay unresolved name strings are NOT supported — pass a
    /// rendering system).
    /// </param>
    public static void RegisterLoaders(AssetSystem assetSystem, RenderingSystem renderingSystem)
    {
        ArgumentNullException.ThrowIfNull(assetSystem);
        ArgumentNullException.ThrowIfNull(renderingSystem);
        assetSystem.RegisterAssetLoader(new AssetLoaderParticleEffect(assetSystem, renderingSystem.ShaderSystem));
    }
}
