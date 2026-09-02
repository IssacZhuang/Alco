using Alco.Rendering;

namespace Alco.Particles;

/// <summary>
/// A material module injects shared shader resources into the render materials a
/// GPU particle system creates (see <see cref="GpuParticleSystem2D.AddMaterialModule"/>
/// and <see cref="GpuParticleSystem3D.AddMaterialModule"/>) — e.g. a lighting
/// middleware binding a light-map texture and its sampling parameters into slots
/// the particle surfaces declare. The particle system stays agnostic of what a
/// module binds: resource slots resolve by name with opt-in semantics
/// (<c>TrySetBuffer</c>/<c>TrySetTexture</c> succeed only where the composed
/// shader declares the slot), so surfaces that declare no matching slots simply
/// ignore a module.
/// </summary>
public interface IParticleMaterialModule
{
    /// <summary>
    /// Binds the module's resources to one render material. Called when the
    /// material is created (after the particle system's own bindings, so a module
    /// may override them), when the module is registered with a system that
    /// already holds materials, and once per material after
    /// <see cref="GpuParticleSystem2D.RefreshMaterialModules"/> or its 3D
    /// counterpart. Implementations must only bind shader resources — never
    /// touch pipeline state (blend/depth) and never dispose the material.
    /// Bindings survive shader hot reloads and pool reallocations (slot values
    /// carry over by resource name), so a module re-applies only after it
    /// replaced its own resource objects.
    /// </summary>
    /// <param name="material">The render material to bind into.</param>
    void ConfigureMaterial(GraphicsMaterial material);
}
