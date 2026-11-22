using Alco.Graphics;

namespace Alco.Effects;

/// <summary>
/// Injects shared shader resources (for example scene lighting textures or buffers)
/// into the render <see cref="Material"/>s created by <see cref="GpuParticleSystem2D"/> or <see cref="GpuParticleSystem3D"/>.
/// Implementations should match optional slots on <c>Material</c> cleanly without failing when a surface declares no matching fields.
/// </summary>
public interface IParticleMaterialModule
{
    /// <summary>Configures shared shader parameters on <paramref name="material"/>.</summary>
    /// <param name="material">The render material to configure.</param>
    void ConfigureMaterial(Material material);
}
