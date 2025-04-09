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
    /// Gets or sets the angular spread of emission cone in degrees (total angle from edge to edge).
    /// </summary>
    public float ConeAngle = 360f;

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
    public ParticleData2D EmitInLocal()
    {
        ParticleData2D particle = default;
        particle.Position = GeneratePosition();

        particle.Color = Color;
        float scale = _random.NextFloat(MinSize, MaxSize);
        particle.Scale = new Vector2(scale, scale);

        // Create normalized random direction vector
        float halfConeAngle = ConeAngle * 0.5f;
        float directionAngle = _random.NextFloat(-halfConeAngle, halfConeAngle);
        Rotation2D direction = new Rotation2D(directionAngle);

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

        particle.Rotation *= new Rotation2D(_random.NextFloat(MinRotation, MaxRotation));

        particle.Lifetime = _random.NextFloat(1, 2);
        particle.Duration = particle.Lifetime;
        return particle;
    }

    public ParticleData2D EmitInWorld(in Transform2D transform)
    {
        ParticleData2D particle = default;
        //generate position in world space
        particle.Position = GeneratePosition() * transform.Scale + transform.Position;

        particle.Color = Color;
        float scale = _random.NextFloat(MinSize, MaxSize);
        // transform the scale to world space
        particle.Scale = new Vector2(scale, scale) * transform.Scale;

        // Create normalized random direction vector
        float halfConeAngle = ConeAngle * 0.5f;
        float directionAngle = _random.NextFloat(-halfConeAngle, halfConeAngle);
        Rotation2D localDirection = new Rotation2D(directionAngle);

        // Apply random speed between MinSpeed and MaxSpeed
        float speed = _random.NextFloat(MinSpeed, MaxSpeed);
        //local velocity
        particle.Velocity = math.rotate(Vector2.UnitX, localDirection) * speed * transform.Scale;
        particle.Velocity = math.rotate(particle.Velocity, transform.Rotation);

        if (IsRotationFollowDirection)
        {
            particle.Rotation = math.direction(particle.Velocity);
        }
        else
        {
            particle.Rotation = Rotation2D.Identity;
        }

        particle.Rotation *= new Rotation2D(_random.NextFloat(MinRotation, MaxRotation));

        particle.Lifetime = _random.NextFloat(1, 2);
        particle.Duration = particle.Lifetime;
        return particle;
    }
}



