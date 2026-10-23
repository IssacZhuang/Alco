using Alco.Rendering;

namespace Alco.Effects;

/// <summary>
/// A material module injects shared shader resources into the render materials a GPU particle system creates (see <see cref="GpuParticleSystem2D.AddMaterialModule"/> and <see cref="GpuParticleSystem3D.AddMaterialModule"/>).
/// Resource slots resolve by name with opt-in semantics, so surfaces that declare no matching slots simply ignore a module.
/// </summary>
public interface IParticleMaterialModule
{
    /// <summary>
    /// Binds the module's resources to one render material; implementations must bind only shader resources, never pipeline state, and must not dispose the material.
    /// Bindings survive shader hot reloads and pool reallocations, so a module re-applies only after replacing its own resource objects.
    /// </summary>
    /// <param name="material">The render material to bind into.</param>
    void ConfigureMaterial(GraphicsMaterial material);
}
