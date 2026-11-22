using System.Text.Json.Serialization.Metadata;
using Alco.Graphics;
using Alco.IO;

namespace Alco.Effects;

/// <summary>
/// The asset-pipeline entry point of Alco.Effects, which registers the particle
/// effect (<c>.afx</c>) asset loader with an asset system.
/// </summary>
public static class ParticleAssetPipeline
{
    /// <summary>The file extension of particle effect assets.</summary>
    public const string EffectExtension = ".afx";

    /// <summary>The module name of the compute pass resetting per-batch draw args.</summary>
    public const string ResetArgsModule = "GpuParticleResetArgs";

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

    /// <summary>The module name of the default particle surface (sprite texture × color).</summary>
    public const string DefaultSurface = "ParticleSurfaceDefault";

    /// <summary>The module name of the default 2D simulation behavior.</summary>
    public const string DefaultBehavior2D = "AlcoParticles_Default2D";

    /// <summary>The module name of the default 3D simulation behavior.</summary>
    public const string DefaultBehavior3D = "AlcoParticles_Default3D";

    /// <summary>
    /// Registers the particle effect asset loader with <paramref name="assetSystem"/>.
    /// </summary>
    /// <param name="assetSystem">The asset system to register the loader with.</param>
    /// <param name="shaderSystem">Optional shader system used to validate behavior module names at load time.</param>
    /// <param name="typeInfoResolver">Optional JSON type-info resolver for asset metadata.</param>
    public static void RegisterLoaders(
        AssetSystem assetSystem,
        ShaderSystem? shaderSystem = null,
        IJsonTypeInfoResolver? typeInfoResolver = null)
    {
        ArgumentNullException.ThrowIfNull(assetSystem);
        assetSystem.RegisterAssetLoader(new AssetLoaderParticleEffect(assetSystem, shaderSystem, typeInfoResolver));
    }
}
