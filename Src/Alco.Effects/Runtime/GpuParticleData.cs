using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Effects;

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

    /// <summary>Flipbook: the first frame of the anim picked at spawn.</summary>
    public uint AnimStart;
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

    /// <summary>Flipbook: the first frame of the anim picked at spawn.</summary>
    public uint AnimStart;
}

/// <summary>
/// Indexed indirect draw argument record (20 bytes; exact twin of <c>GpuDrawArgs</c> in AlcoParticles_Core2D.slang).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuDrawArgs
{
    /// <summary>Number of indices per quad instance (6).</summary>
    public uint IndexCount;

    /// <summary>Atomically accumulated surviving particle count for this batch.</summary>
    public uint InstanceCount;

    /// <summary>Starting index in the shared quad index buffer (0).</summary>
    public uint FirstIndex;

    /// <summary>Signed offset added to each fetched vertex index (0).</summary>
    public int VertexOffset;

    /// <summary>Starting instance index (always 0 for cross-backend portability).</summary>
    public uint FirstInstance;
}

/// <summary>
/// Per-instance particle lookup record (8 bytes; exact twin of <c>GpuInstanceData</c> in AlcoParticles_Core2D.slang).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuInstanceData
{
    /// <summary>Emitter slot in the system's parameter buffer.</summary>
    public uint EmitterSlot;

    /// <summary>Local particle slot inside the emitter's pool slice.</summary>
    public uint ParticleIndex;
}

/// <summary>
/// One 64-thread workgroup assignment for batched emit/simulate compute dispatches
/// (16 bytes; exact twin of <c>GpuWorkBlock</c> in AlcoParticles_Core2D.slang).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuParticleWorkBlock
{
    /// <summary>Emitter slot processed by this workgroup.</summary>
    public uint EmitterSlot;

    /// <summary>Draw batch index in the per-frame draw arguments buffer.</summary>
    public uint DrawIndex;

    /// <summary>Base record offset for the batch inside the instance data buffer.</summary>
    public uint DrawStart;

    /// <summary>First local particle/spawn thread index for this 64-thread block.</summary>
    public uint ThreadBase;
}
