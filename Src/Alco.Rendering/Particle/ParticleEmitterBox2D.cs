
using System.Numerics;

namespace Alco.Rendering;

public class ParticleEmitterBox2D : IParticleEmitter<ParticleData2D>
{
    private readonly Random _random = new Random(3151344);
    public Vector2 Position;
    public Vector2 Extents;
    public Rotation2D Rotation;

    public ParticleEmitterBox2D()
    {
        Position = Vector2.Zero;
        Extents = Vector2.One;
    }

    public ParticleEmitterBox2D(Vector2 position, Vector2 extents)
    {
        Position = position;
        Extents = extents;
    }

    public ParticleData2D Emit()
    {
        ParticleData2D particle = default;
        particle.Position = Position + new Vector2(_random.NextFloat(-Extents.X, Extents.X), _random.NextFloat(-Extents.Y, Extents.Y));
        particle.Position = math.rotate(particle.Position, Rotation);
        particle.Rotation = Rotation2D.Identity;
        particle.Color = new Vector4(1, 1, 1, 1);
        particle.Size = 1;
        particle.Velocity = new Vector2(_random.NextFloat(-1, 1), _random.NextFloat(-1, 1));
        particle.Lifetime = _random.NextFloat(1, 2);
        return particle;
    }

}

