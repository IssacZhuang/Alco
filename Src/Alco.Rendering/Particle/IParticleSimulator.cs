
namespace Alco.Rendering;

public interface IParticleSimulator
{
    void Simulate(ref ParticleData2D particle, float deltaTime);
}

