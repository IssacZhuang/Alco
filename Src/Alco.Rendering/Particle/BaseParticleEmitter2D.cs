using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;


public abstract class BaseParticleEmitter2D : IParticleEmitter2D
{
    private FastRandom _random = FastRandom.CreateFromIndex(3);

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
    /// The lifetime of the particle.
    /// </summary>
    public float Lifetime = 1.0f;

    /// <summary>
    /// Gets or sets whether the rotation should follow the direction of the velocity.
    /// </summary>
    public bool IsRotationFollowDirection = true;

    protected ref FastRandom Random => ref _random;

    protected abstract Vector2 GeneratePosition();


    public ParticleData2D Emit(in Transform2D transform)
    {
        ParticleData2D particle = default;
        //generate position in world space
        particle.Transform.Position = GeneratePosition() * transform.Scale + transform.Position;

        particle.Color = Color;
        float scale = _random.NextFloat(MinSize, MaxSize);
        // transform the scale to world space
        particle.Transform.Scale = new Vector2(scale, scale) * transform.Scale;

        // Create normalized random direction vector
        float halfConeAngle = ConeAngle * 0.5f;
        float directionAngle = _random.NextFloat(-halfConeAngle, halfConeAngle);
        Rotation2D direction = new Rotation2D(directionAngle) * transform.Rotation;

        // Apply random speed between MinSpeed and MaxSpeed
        float speed = _random.NextFloat(MinSpeed, MaxSpeed);
        //local velocity
        particle.Velocity = math.rotate(Vector2.UnitX, direction) * speed * transform.Scale;

        if (IsRotationFollowDirection)
        {
            //particle.Transform.Rotation = math.direction(particle.Velocity);
            particle.Transform.Rotation = direction;
        }
        else
        {
            particle.Transform.Rotation = Rotation2D.Identity;
        }

        particle.Transform.Rotation *= new Rotation2D(_random.NextFloat(MinRotation, MaxRotation));
        particle.Lifetime = Lifetime;
        particle.Duration = Lifetime;
        return particle;
    }
}



