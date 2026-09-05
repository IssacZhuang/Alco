using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Particles;

/// <summary>
/// The per-emitter parameter record of a 3D particle group; the 3D counterpart of
/// <see cref="EmitterParams2D"/>. Exact twin of the slang <c>EmitterParams3D</c>
/// struct (AlcoParticles_Core3D.slang) — the field order and padding must match.
/// The total size must stay a multiple of 16: the buffer element stride rounds up
/// to the struct alignment (16, from the matrix), and a CPU/GPU size mismatch
/// shifts every emitter's parameters.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EmitterParams3D
{
    /// <summary>The number of particles to emit this frame.</summary>
    public uint SpawnCount;

    /// <summary>The ring-buffer write cursor of this frame's emission.</summary>
    public uint EmitCursor;

    /// <summary>The capacity of the emitter's pool slice.</summary>
    public uint Capacity;

    /// <summary>The absolute base slot of the emitter's slice in the shared pool.</summary>
    public uint SliceOffset;

    /// <summary>The frame's delta time in seconds.</summary>
    public float DeltaTime;

    /// <summary>The emitter's timeline position in seconds.</summary>
    public float EmitterTime;

    /// <summary>The per-frame RNG seed of the spawn pass.</summary>
    public uint FrameSeed;

    /// <summary>
    /// Bit flags; bit0: world-space simulation (spawn through <see cref="WorldMatrix"/>),
    /// bit1: color-gradient lookup bound, bit2: size-curve lookup bound,
    /// bit3: velocity-stretched billboards,
    /// bit4: flipbook plays in reverse (last frame at spawn).
    /// The slang side mirrors these as the PARTICLE_FLAG_* constants
    /// (AlcoParticles_Core2D.slang).
    /// </summary>
    public uint Flags;

    /// <summary>The emitter's transform matrix (used at spawn in world space, at draw in local space).</summary>
    public Matrix4x4 WorldMatrix;

    /// <summary>Emission shape: x = type (0 point, 1 sphere, 2 hemisphere, 3 box), y = radius, z = inner radius fraction.</summary>
    public Vector4 Shape;

    /// <summary>Box half extents (xyz).</summary>
    public Vector4 Extents;

    /// <summary>Emission direction: xyz = base direction, w = cone spread half-angle (rad).</summary>
    public Vector4 Emission;

    /// <summary>Speed: x = min, y = max, z = direction mode (0 constant, 1 radial), w = velocity-stretch speed scale.</summary>
    public Vector4 Speed;

    /// <summary>Life: x = lifetime min, y = lifetime max, z = fade-in fraction, w = fade-out fraction.</summary>
    public Vector4 Life;

    /// <summary>Size: x = min, y = max, z = end scale multiplier, w = velocity-stretch length scale.</summary>
    public Vector4 Size;

    /// <summary>Billboard roll: x = start roll min, y = max, z = roll rate min, w = max.</summary>
    public Vector4 Rotation;

    /// <summary>Motion: xyz = gravity, w = drag.</summary>
    public Vector4 Motion;

    /// <summary>Spawn color range lower bound.</summary>
    public ColorFloat ColorMin;

    /// <summary>Spawn color range upper bound.</summary>
    public ColorFloat ColorMax;

    /// <summary>The color the particles lerp to at the end of their life.</summary>
    public ColorFloat ColorEnd;

    /// <summary>The global color multiplier of the group's material.</summary>
    public ColorFloat Tint;

    /// <summary>Flipbook: x = rows, y = cols, z = lifetime-relative cycle count, w = frames per anim (0 = whole sheet).</summary>
    public Vector4 Flipbook;

    /// <summary>The quad mesh's index count (written into the indirect draw record).</summary>
    public uint IndexCount;

    /// <summary>
    /// A custom per-instance data channel for custom surface vertex hooks, the 3D
    /// counterpart of <see cref="EmitterParams2D.CustomData"/>
    /// (<c>IParticleSurface.adjustWorldPosition</c>, surfaced as
    /// <c>ParticleVertexInput.customData</c>). Set per instance through
    /// <see cref="ParticleEffectInstance3D.SetGroupParams"/>.
    /// </summary>
    public float CustomData;

    /// <summary>Reserved.</summary>
    public uint Reserved1;

    /// <summary>Reserved.</summary>
    public uint Reserved2;

    /// <summary>
    /// The fixed spawn offset in emitter-local space: shifts every spawned
    /// particle by this vector before the world transform, so it follows the
    /// emitter's rotation in both simulation spaces. Offsets a directional
    /// effect away from its anchor (e.g. a muzzle flash off the muzzle point).
    /// Must stay the last slang member (16-byte aligned): HLSL packs a scalar
    /// after a float3 into its w slot while std430 pushes it to the next row,
    /// so the pad below is CPU-side only.
    /// </summary>
    public Vector3 PositionOffset;

    /// <summary>
    /// Trailing CPU-side pad that keeps the struct size a multiple of 16 — the
    /// buffer element stride on the slang side; the shaders never read it.
    /// </summary>
    public float Reserved3;

    /// <summary>The world-space-simulation flag bit of <see cref="Flags"/>.</summary>
    public const uint FlagWorldSpace = 1u;

    /// <summary>The color-gradient-lookup flag bit of <see cref="Flags"/>.</summary>
    public const uint FlagColorGradient = 2u;

    /// <summary>The size-curve-lookup flag bit of <see cref="Flags"/>.</summary>
    public const uint FlagSizeCurve = 4u;

    /// <summary>The velocity-stretch flag bit of <see cref="Flags"/>.</summary>
    public const uint FlagVelocityStretch = 8u;

    /// <summary>The reversed-flipbook flag bit of <see cref="Flags"/>.</summary>
    public const uint FlagFlipbookReverse = 16u;

    /// <summary>
    /// Merges an edited record into the live slot record of an emitter: the static
    /// (asset-authored) fields come from <paramref name="edited"/> while the
    /// slot-bound (<see cref="Capacity"/>, <see cref="SliceOffset"/>,
    /// <see cref="IndexCount"/>) and per-frame (<see cref="SpawnCount"/>,
    /// <see cref="EmitCursor"/>, <see cref="DeltaTime"/>, <see cref="EmitterTime"/>,
    /// <see cref="FrameSeed"/>, <see cref="WorldMatrix"/>) fields keep their live
    /// values. Backs <see cref="ParticleEffectInstance3D.SetGroupParams"/>.
    /// </summary>
    /// <param name="live">The current slot record.</param>
    /// <param name="edited">The record carrying the edited static fields.</param>
    /// <returns>The merged record to store back into the slot.</returns>
    internal static EmitterParams3D MergeEdited(in EmitterParams3D live, in EmitterParams3D edited)
    {
        EmitterParams3D merged = edited;
        merged.SpawnCount = live.SpawnCount;
        merged.EmitCursor = live.EmitCursor;
        merged.Capacity = live.Capacity;
        merged.SliceOffset = live.SliceOffset;
        merged.DeltaTime = live.DeltaTime;
        merged.EmitterTime = live.EmitterTime;
        merged.FrameSeed = live.FrameSeed;
        merged.WorldMatrix = live.WorldMatrix;
        merged.IndexCount = live.IndexCount;
        return merged;
    }

    /// <summary>
    /// Fills the static (asset-authored) fields of the record from a group asset;
    /// the per-frame fields (control, timing, matrix) are written by the instance.
    /// </summary>
    /// <param name="group">The group asset.</param>
    /// <param name="indexCount">The quad mesh's index count.</param>
    /// <returns>The filled record.</returns>
    public static EmitterParams3D FromAsset(ParticleGroup3D group, uint indexCount)
    {
        ArgumentNullException.ThrowIfNull(group);
        EmitterParams3D parameters = default;
        parameters.WorldMatrix = Matrix4x4.Identity;
        parameters.Shape = new Vector4(
            (float)group.Shape.Type,
            group.Shape.Radius,
            Math.Clamp(group.Shape.InnerRadius, 0f, 1f),
            0f);
        parameters.Extents = new Vector4(group.Shape.Extents, 0f);
        parameters.Emission = new Vector4(group.Direction, group.SpreadAngle);
        parameters.Speed = new Vector4(
            group.Speed.Min,
            group.Speed.Max,
            (float)group.DirectionMode,
            group.StretchSpeedScale);
        parameters.Life = new Vector4(
            group.Lifetime.Min,
            group.Lifetime.Max,
            Math.Clamp(group.FadeIn, 0f, 1f),
            Math.Clamp(group.FadeOut, 0f, 1f));
        parameters.Size = new Vector4(group.Size.Min, group.Size.Max, group.EndScale, group.StretchLengthScale);
        parameters.Rotation = new Vector4(
            group.StartRotation.Min,
            group.StartRotation.Max,
            group.AngularVelocity.Min,
            group.AngularVelocity.Max);
        parameters.Motion = new Vector4(group.Gravity, group.Drag);
        parameters.ColorMin = group.StartColor.Min;
        parameters.ColorMax = group.StartColor.Max;
        parameters.ColorEnd = group.EndColor;
        parameters.Tint = group.Tint;
        ParticleFlipbook? flipbook = group.Flipbook;
        parameters.Flipbook = flipbook != null
            ? new Vector4(
                flipbook.Rows,
                flipbook.Cols,
                flipbook.Cycles,
                Math.Clamp(flipbook.FramesPerAnim, 0, flipbook.Rows * flipbook.Cols))
            : new Vector4(1f, 1f, 0f, 0f);
        parameters.IndexCount = indexCount;
        parameters.PositionOffset = group.PositionOffset;
        parameters.Flags = group.SimulationSpace == ParticleSimulationSpace.World ? FlagWorldSpace : 0u;
        if (group.ColorGradient is { Count: > 0 })
        {
            parameters.Flags |= FlagColorGradient;
        }
        if (group.SizeCurve is { Count: > 0 })
        {
            parameters.Flags |= FlagSizeCurve;
        }
        if (group.VelocityStretch)
        {
            parameters.Flags |= FlagVelocityStretch;
        }
        if (flipbook is { Reverse: true })
        {
            parameters.Flags |= FlagFlipbookReverse;
        }
        return parameters;
    }
}
