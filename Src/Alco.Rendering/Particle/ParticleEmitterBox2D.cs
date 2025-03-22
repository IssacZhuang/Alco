using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

public class ParticleEmitterBox2D : IParticleEmitter2D
{
    private Random _random = Random.CreateFromIndex(3);
    public Vector2 Position;
    public Vector2 Extents;
    public Rotation2D Rotation;


    public float MinSpeed = 1.0f;
    public float MaxSpeed = 2.0f;

    public float MinRotation = 0.0f;
    public float MaxRotation = 360.0f;

    public ColorFloat Color = new ColorFloat(1, 1, 1, 1);

    public float MinSize = 1.0f;
    public float MaxSize = 1.0f;

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
        particle.Rotation = Rotation2D.FromDegree(_random.NextFloat(MinRotation, MaxRotation));
        particle.Color = Color;
        particle.Size = _random.NextFloat(MinSize, MaxSize);

        // Create normalized random direction vector
        Rotation2D direction = _random.NextRotation2D();

        // Apply random speed between MinSpeed and MaxSpeed
        float speed = _random.NextFloat(MinSpeed, MaxSpeed);
        particle.Velocity = math.rotate(Vector2.UnitX, direction) * speed;

        particle.Lifetime = _random.NextFloat(1, 2);
        return particle;
    }

}

