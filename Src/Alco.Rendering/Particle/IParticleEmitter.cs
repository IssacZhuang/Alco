namespace Alco.Rendering;

/// <summary>
/// Represents a 2D particle emitter interface that generates particle data.
/// All emission is performed in world space.
/// </summary>
public interface IParticleEmitter2D
{
    /// <summary>
    /// Emits a new particle and returns its data in world space.
    /// </summary>
    /// <param name="transform">The transform of the particle system.</param>
    /// <returns>A <see cref="ParticleData2D"/> containing the emitted particle's properties.</returns>
    ParticleData2D Emit(in Transform2D transform);
}
