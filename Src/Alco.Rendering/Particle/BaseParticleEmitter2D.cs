

using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;


public abstract class BaseParticleEmitter2D : IParticleEmitter2D
{
    private Random _random = Random.CreateFromIndex(3);

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
    public float MaxRotation = 360f;

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
    /// Gets or sets whether the rotation should follow the direction of the velocity.
    /// </summary>
    public bool IsRotationFollowDirection = true;

    protected ref Random Random => ref _random;

    protected abstract Vector2 GeneratePosition();


    /// <summary>
    /// Emits a single particle with randomized properties within the configured ranges.
    /// </summary>
    /// <returns>A new particle with randomized position, velocity, rotation, color, size, and lifetime.</returns>
    public ParticleData2D Emit()
    {
        ParticleData2D particle = default;
        particle.Position = GeneratePosition();

        particle.Color = Color;
        particle.Size = _random.NextFloat(MinSize, MaxSize);

        // Create normalized random direction vector
        Rotation2D direction = _random.NextRotation2D();

        // Apply random speed between MinSpeed and MaxSpeed
        float speed = _random.NextFloat(MinSpeed, MaxSpeed);
        particle.Velocity = math.rotate(Vector2.UnitX, direction) * speed;

        if (IsRotationFollowDirection)
        {
            particle.Rotation = math.inverse(direction);
        }
        else
        {
            particle.Rotation = Rotation2D.Identity;
        }

        particle.Rotation *= Rotation2D.FromDegree(_random.NextFloat(MinRotation, MaxRotation));

        particle.Lifetime = _random.NextFloat(1, 2);
        return particle;
    }
}



