using Alco.IO;
using Alco.Rendering;

namespace Alco.Effects;

/// <summary>
/// The asset-pipeline entry point of Alco.Effects, which registers the particle
/// effect (<c>.afx</c>) asset loader with an asset system.
/// </summary>
public static class ParticleAssetPipeline
{
    /// <summary>The file extension of particle effect assets.</summary>
    public const string EffectExtension = ".afx";

    /// <summary>The module name of the built-in 2D particle render pass template.</summary>
    public const string RenderModule2D = "GpuParticle2D";

    /// <summary>The module name of the built-in 3D particle render pass template.</summary>
    public const string RenderModule3D = "GpuParticle3D";

    /// <summary>The module name of the built-in 2D emit pass template.</summary>
    public const string EmitModule2D = "GpuParticleEmit2D";

    /// <summary>The module name of the built-in 3D emit pass template.</summary>
    public const string EmitModule3D = "GpuParticleEmit3D";

    /// <summary>The module name of the built-in 2D simulate pass template.</summary>
    public const string SimulateModule2D = "GpuParticleSimulate2D";

    /// <summary>The module name of the built-in 3D simulate pass template.</summary>
    public const string SimulateModule3D = "GpuParticleSimulate3D";

    /// <summary>The module name of the built-in 2D init pass (slice-recycle kills).</summary>
    public const string InitModule2D = "GpuParticleInit2D";

    /// <summary>The module name of the built-in 3D init pass (slice-recycle kills).</summary>
    public const string InitModule3D = "GpuParticleInit3D";

    /// <summary>
    /// The module name of the default particle surface (texture × particle color),
    /// composed with the render pass templates for groups whose material names no
    /// surface module of its own.
    /// </summary>
    public const string DefaultSurface = "ParticleSurfaceDefault";

    /// <summary>
    /// The module name of the default 2D simulation behavior (used by 2D groups
    /// without an explicit <see cref="ParticleGroup.Behavior"/>).
    /// </summary>
    public const string DefaultBehavior2D = "AlcoParticles-Default2D";

    /// <summary>
    /// The module name of the default 3D simulation behavior (used by 3D groups
    /// without an explicit <see cref="ParticleGroup.Behavior"/>).
    /// </summary>
    public const string DefaultBehavior3D = "AlcoParticles-Default3D";

    /// <summary>
    /// Registers the particle effect asset loader with the asset system.
    /// </summary>
    /// <param name="assetSystem">The asset system to register the loader with.</param>
    /// <param name="renderingSystem">The rendering system whose shader system resolves shader module references at load time.</param>
    /// <param name="typeInfoResolver">The optional JSON type-info resolver used for asset metadata.</param>
    public static void RegisterLoaders(AssetSystem assetSystem, RenderingSystem renderingSystem,
        System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        ArgumentNullException.ThrowIfNull(assetSystem);
        ArgumentNullException.ThrowIfNull(renderingSystem);
        assetSystem.RegisterAssetLoader(new AssetLoaderParticleEffect(assetSystem, renderingSystem.ShaderSystem, typeInfoResolver));
    }
}
