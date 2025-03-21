
namespace Alco.Rendering;

public interface IParticleEmitter<TParticleData> where TParticleData : unmanaged
{
    TParticleData Emit();
}
