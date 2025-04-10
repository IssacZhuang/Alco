namespace Alco.Rendering;

/// <summary>
/// Defines a 2D particle simulator interface for particle system simulation.
/// </summary>
public interface IParticleSimulator2D
{
    /// <summary>
    /// Simulates the behavior of a single particle in a 2D particle system.
    /// </summary>
    /// <param name="system">The particle system that contains the particle.</param>
    /// <param name="particle">The particle data to be simulated.</param>
    /// <param name="deltaTime">The time step for the simulation.</param>
    void SimulateInLocal(ParticleSystem2DCPU system, ref ParticleData2D particle, float deltaTime);

    /// <summary>
    /// Simulates the behavior of a single particle in a 2D particle system.
    /// </summary>
    /// <param name="system">The particle system that contains the particle.</param>
    /// <param name="particle">The particle data to be simulated.</param>
    /// <param name="deltaTime">The time step for the simulation.</param>
    void SimulateInWorld(ParticleSystem2DCPU system, ref ParticleData2D particle, float deltaTime);
}

