using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Represents 2D particle data containing position, velocity, color, rotation, lifetime, and size information.
/// </summary>
public struct ParticleData2D
{
    /// <summary>
    /// Gets or sets the 2D position of the particle.
    /// </summary>
    public Vector2 Position;

    /// <summary>
    /// Gets or sets the 2D velocity vector of the particle.
    /// </summary>
    public Vector2 Velocity;

    /// <summary>
    /// Gets or sets the RGBA color of the particle.
    /// </summary>
    public Vector4 Color;

    /// <summary>
    /// Gets or sets the 2D rotation of the particle.
    /// </summary>
    public Rotation2D Rotation;

    /// <summary>
    /// Gets or sets the remaining lifetime of the particle in seconds.
    /// </summary>
    public float Lifetime;

    /// <summary>
    /// Gets or sets the size (scale) of the particle.
    /// </summary>
    public float Size;
}

