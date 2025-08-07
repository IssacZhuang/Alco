using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Simulates particles by lerping between two colors over the particle's lifetime.
/// </summary>
public sealed class ParticleSimulatorColorLerp2D : IParticleSimulator2D
{
    /// <summary>
    /// The initial color of the particle.
    /// </summary>
    public ColorFloat StartColor = new ColorFloat(1, 1, 1, 1);

    /// <summary>
    /// The final color of the particle.
    /// </summary>
    public ColorFloat EndColor = new ColorFloat(1, 1, 1, 0f);


    public void Simulate(ParticleSystem2DCPU system, ref ParticleData2D particle, float deltaTime)
    {
        particle.Transform.Position += particle.Velocity * deltaTime;
        particle.Lifetime -= deltaTime;
        float t = particle.Lifetime / particle.Duration;
        //lifetime is decreasing, so we need to lerp from end to start
        particle.Color = ColorFloat.Lerp(EndColor, StartColor, t);
    }
}
