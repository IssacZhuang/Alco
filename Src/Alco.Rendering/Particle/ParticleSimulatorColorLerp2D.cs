
using Alco.Graphics;

namespace Alco.Rendering;

public sealed class ParticleSimulatorColorLerp2D : IParticleSimulator2D
{
    public ColorFloat StartColor = new ColorFloat(1, 1, 1, 1);
    public ColorFloat EndColor = new ColorFloat(1, 1, 1, 0f);

    public void Simulate(ParticleSystem2DCPU system, ref ParticleData2D particle, float deltaTime)
    {
        particle.Position += particle.Velocity * deltaTime;
        particle.Lifetime -= deltaTime;
        float t = particle.Lifetime / system.ParticleLifetime;
        //lifetime is decreasing, so we need to lerp from end to start
        particle.Color = ColorFloat.Lerp(EndColor, StartColor, t);
    }
}
