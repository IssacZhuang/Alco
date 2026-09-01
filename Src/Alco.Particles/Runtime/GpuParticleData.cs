using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Particles;

/// <summary>
/// The 2D GPU particle record (one pool slot). Exact twin of the slang
/// <c>ParticleData2D</c> struct (AlcoParticles_Core2D.slang); written and read
/// only by the GPU — the CPU never inspects pool contents.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GpuParticle2D
{
    /// <summary>World (or emitter-local) position.</summary>
    public Vector2 Position;

    /// <summary>Current quad extents.</summary>
    public Vector2 Scale;

    /// <summary>Current color (color-over-life and fades applied).</summary>
    public ColorFloat Color;

    /// <summary>Spawn color, the lerp source of color-over-life.</summary>
    public ColorFloat StartColor;

    /// <summary>Current velocity.</summary>
    public Vector2 Velocity;

    /// <summary>Spawn scale, the lerp source of size-over-life.</summary>
    public Vector2 StartScale;

    /// <summary>Rotation in radians.</summary>
    public float Rotation;

    /// <summary>Angular velocity in radians per second.</summary>
    public float AngularVelocity;

    /// <summary>Height above the ground plane in world (or emitter-local) units.</summary>
    public float Height;

    /// <summary>Current height velocity in world units per second.</summary>
    public float HeightVelocity;

    /// <summary>Remaining lifetime in seconds; &lt;= 0 marks a dead (free) slot.</summary>
    public float Lifetime;

    /// <summary>Total lifetime in seconds.</summary>
    public float Duration;
}

/// <summary>
/// The 3D GPU particle record (one pool slot). Exact twin of the slang
/// <c>ParticleData3D</c> struct (AlcoParticles_Core3D.slang); written and read
/// only by the GPU — the CPU never inspects pool contents.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GpuParticle3D
{
    /// <summary>World (or emitter-local) position.</summary>
    public Vector3 Position;

    /// <summary>Current quad extent (uniform).</summary>
    public float Size;

    /// <summary>Current color (color-over-life and fades applied).</summary>
    public ColorFloat Color;

    /// <summary>Spawn color, the lerp source of color-over-life.</summary>
    public ColorFloat StartColor;

    /// <summary>Current velocity.</summary>
    public Vector3 Velocity;

    /// <summary>Spawn size, the lerp source of size-over-life.</summary>
    public float StartSize;

    /// <summary>Billboard roll in radians.</summary>
    public float Rotation;

    /// <summary>Billboard roll velocity in radians per second.</summary>
    public float RollRate;

    /// <summary>Remaining lifetime in seconds; &lt;= 0 marks a dead (free) slot.</summary>
    public float Lifetime;

    /// <summary>Total lifetime in seconds.</summary>
    public float Duration;
}
