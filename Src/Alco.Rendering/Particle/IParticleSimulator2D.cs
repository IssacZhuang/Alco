
namespace Alco.Rendering;

public interface IParticleSimulator2D
{
    void Simulate(ParticleSystem2DCPU system, ref ParticleData2D particle, float deltaTime);
}

