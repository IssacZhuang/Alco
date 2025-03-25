using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Represents a 2D particle emitter in the shape of a box that generates particles within specified boundaries.
/// </summary>
public sealed class ParticleEmitterBox2D : BaseParticleEmitter2D
{

    /// <summary>
    /// The shape of the emitter.
    /// </summary>
    public ShapeBox2D Shape;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="ParticleEmitterBox2D"/> class with default position and extents.
    /// </summary>
    public ParticleEmitterBox2D()
    {
        Shape = new ShapeBox2D(Vector2.Zero, Vector2.Zero);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ParticleEmitterBox2D"/> class with specified position and extents.
    /// </summary>
    /// <param name="position">The center position of the emission box.</param>
    /// <param name="extents">The half-dimensions of the emission box.</param>
    public ParticleEmitterBox2D(Vector2 position, Vector2 extents)
    {
        Shape = new ShapeBox2D(position, extents);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected override Vector2 GeneratePosition()
    {
        Vector2 position = Shape.Center + new Vector2(Random.NextFloat(-Shape.Extends.X, Shape.Extends.X), Random.NextFloat(-Shape.Extends.Y, Shape.Extends.Y));
        position = math.rotate(position, Shape.Rotation);
        return position;
    }
}

