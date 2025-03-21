
namespace Alco.Rendering;

// particle system factory

public partial class RenderingSystem
{

    /// <summary>
    /// Create a particle system that simulates particles on the CPU.
    /// </summary>
    /// <param name="mesh">The mesh to use for the particle system.</param>
    /// <param name="material">The material to use for the particle system.</param>
    /// <param name="emitter">The emitter to use for the particle system.</param>
    /// <returns>The created particle system.</returns>
    public ParticleSystem2DCPU CreateParticleSystem2DCPU(Mesh mesh, Material material, IParticleEmitter<ParticleData2D> emitter)
    {
        return new ParticleSystem2DCPU(this, mesh, material, emitter);
    }

    /// <summary>
    /// Create a particle system that simulates particles on the CPU.
    /// </summary>
    /// <param name="material">The material to use for the particle system.</param>
    /// <param name="emitter">The emitter to use for the particle system.</param>
    /// <returns>The created particle system.</returns>
    public ParticleSystem2DCPU CreateParticleSystem2DCPU(Material material, IParticleEmitter<ParticleData2D> emitter)
    {
        return new ParticleSystem2DCPU(this, MeshCenteredSprite, material, emitter);
    }

}
