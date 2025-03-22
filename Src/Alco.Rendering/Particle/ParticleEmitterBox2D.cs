using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Represents a 2D particle emitter in the shape of a box that generates particles within specified boundaries.
/// </summary>
public class ParticleEmitterBox2D : IParticleEmitter2D
{
    private Random _random = Random.CreateFromIndex(3);

    /// <summary>
    /// Gets or sets the center position of the emission box.
    /// </summary>
    public Vector2 Position;

    /// <summary>
    /// Gets or sets the half-dimensions of the emission box.
    /// </summary>
    public Vector2 Extents;

    /// <summary>
    /// Gets or sets the rotation of the emission box.
    /// </summary>
    public Rotation2D Rotation;

    /// <summary>
    /// Gets or sets the minimum speed of emitted particles.
    /// </summary>
    public float MinSpeed = 1.0f;

    /// <summary>
    /// Gets or sets the maximum speed of emitted particles.
    /// </summary>
    public float MaxSpeed = 2.0f;

    /// <summary>
    /// Gets or sets the minimum rotation angle in degrees for emitted particles.
    /// </summary>
    public float MinRotation = 0.0f;

    /// <summary>
    /// Gets or sets the maximum rotation angle in degrees for emitted particles.
    /// </summary>
    public float MaxRotation = 360.0f;

    /// <summary>
    /// Gets or sets the color of emitted particles.
    /// </summary>
    public ColorFloat Color = new ColorFloat(1, 1, 1, 1);

    /// <summary>
    /// Gets or sets the minimum size of emitted particles.
    /// </summary>
    public float MinSize = 1.0f;

    /// <summary>
    /// Gets or sets the maximum size of emitted particles.
    /// </summary>
    public float MaxSize = 1.0f;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticleEmitterBox2D"/> class with default position and extents.
    /// </summary>
    public ParticleEmitterBox2D()
    {
        Position = Vector2.Zero;
        Extents = Vector2.One;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticleEmitterBox2D"/> class with specified position and extents.
    /// </summary>
    /// <param name="position">The center position of the emission box.</param>
    /// <param name="extents">The half-dimensions of the emission box.</param>
    public ParticleEmitterBox2D(Vector2 position, Vector2 extents)
    {
        Position = position;
        Extents = extents;
    }

    /// <summary>
    /// Emits a single particle with randomized properties within the configured ranges.
    /// </summary>
    /// <returns>A new particle with randomized position, velocity, rotation, color, size, and lifetime.</returns>
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

