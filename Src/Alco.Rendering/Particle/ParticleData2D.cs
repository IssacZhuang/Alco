using System.Numerics;

namespace Alco.Rendering;

/// <summary>
/// Represents 2D particle data containing position, velocity, color, rotation, lifetime, and size information.
/// <br/> Also a same struct in Particle2D.hlsl shader
/// </summary>
public struct ParticleData2D
{
    public Transform2D Transform;

    /// <summary>
    /// Gets or sets the 2D velocity vector of the particle.
    /// </summary>
    public Vector2 Velocity;

    /// <summary>
    /// Gets or sets the RGBA color of the particle.
    /// </summary>
    public Vector4 Color;

    /// <summary>
    /// Gets or sets the remaining lifetime of the particle in seconds.
    /// Start form <see cref="Duration"/> to 0.
    /// </summary>
    public float Lifetime;

    /// <summary>
    /// The duration of the particle in seconds.
    /// </summary>
    public float Duration;

    /// <summary>
    /// The offset of z axis of the particle.
    /// </summary>
    public float ZOffset;

    /// <summary>
    /// for memory alignment to GPU
    /// </summary>
    public float _reserved;
}

