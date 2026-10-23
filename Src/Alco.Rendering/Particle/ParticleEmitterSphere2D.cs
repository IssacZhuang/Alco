
using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// A particle emitter that emits particles from a sphere.
/// </summary>
public sealed class ParticleEmitterSphere2D : BaseParticleEmitter2D
{
    /// <summary>
    /// The shape of the sphere.
    /// </summary>
    public ShapeSphere2D Shape;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticleEmitterSphere2D"/> class.
    /// </summary>
    /// <param name="radius">The sphere radius.</param>
    public ParticleEmitterSphere2D(float radius)
    {
        Shape = new ShapeSphere2D(Vector2.Zero, radius);
    }

    protected override Vector2 GeneratePosition()
    {
        Rotation2D rotation = Random.NextRotation2D();
        float distance = Shape.Radius * math.sqrt(Random.NextFloat());
        Vector2 position = Shape.Center + new Vector2(distance * rotation.S, distance * rotation.C);
        return position;
    }

}
