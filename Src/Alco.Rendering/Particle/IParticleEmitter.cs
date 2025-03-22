namespace Alco.Rendering;

/// <summary>
/// Represents a 2D particle emitter interface that generates particle data.
/// </summary>
public interface IParticleEmitter2D
{
    /// <summary>
    /// Emits a new particle and returns its data.
    /// </summary>
    /// <returns>A <see cref="ParticleData2D"/> containing the emitted particle's properties.</returns>
    ParticleData2D Emit();
}
