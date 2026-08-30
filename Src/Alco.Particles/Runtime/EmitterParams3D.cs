using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Particles;

/// <summary>
/// The per-emitter parameter record of a 3D particle group; the 3D counterpart of
/// <see cref="EmitterParams2D"/>. Exact twin of the slang <c>EmitterParams3D</c>
/// struct (AlcoParticles_Core3D.slang) — the field order and padding must match.
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

    /// <summary>Bit flags; bit0: world-space simulation (spawn through <see cref="WorldMatrix"/>).</summary>
    public uint Flags;

    /// <summary>The emitter's transform matrix (used at spawn in world space, at draw in local space).</summary>
    public Matrix4x4 WorldMatrix;

    /// <summary>Emission shape: x = type (0 point, 1 sphere, 2 hemisphere, 3 box), y = radius, z = inner radius fraction.</summary>
    public Vector4 Shape;

    /// <summary>Box half extents (xyz).</summary>
    public Vector4 Extents;

    /// <summary>Emission direction: xyz = base direction, w = cone spread half-angle (rad).</summary>
    public Vector4 Emission;

    /// <summary>Speed: x = min, y = max, z = direction mode (0 constant, 1 radial).</summary>
    public Vector4 Speed;

    /// <summary>Life: x = lifetime min, y = lifetime max, z = fade-in fraction, w = fade-out fraction.</summary>
    public Vector4 Life;

    /// <summary>Size: x = min, y = max, z = end scale multiplier.</summary>
    public Vector4 Size;

    /// <summary>Billboard roll: x = start roll min, y = max, z = roll rate min, w = max.</summary>
    public Vector4 Rotation;

    /// <summary>Motion: xyz = gravity, w = drag.</summary>
    public Vector4 Motion;

    /// <summary>Spawn color range lower bound.</summary>
    public Vector4 ColorMin;

    /// <summary>Spawn color range upper bound.</summary>
    public Vector4 ColorMax;

    /// <summary>The color the particles lerp to at the end of their life.</summary>
    public Vector4 ColorEnd;

    /// <summary>The global color multiplier of the group's material.</summary>
    public Vector4 Tint;

    /// <summary>Flipbook: x = rows, y = cols, z = fps, w = loop (0/1).</summary>
    public Vector4 Flipbook;

    /// <summary>The quad mesh's index count (written into the indirect draw record).</summary>
    public uint IndexCount;

    /// <summary>Reserved.</summary>
    public uint Reserved0;

    /// <summary>Reserved.</summary>
    public uint Reserved1;

    /// <summary>Reserved.</summary>
    public uint Reserved2;

    /// <summary>The world-space-simulation flag bit of <see cref="Flags"/>.</summary>
    public const uint FlagWorldSpace = 1u;

    /// <summary>
    /// Fills the static (asset-authored) fields of the record from a group asset;
    /// the per-frame fields (control, timing, matrix) are written by the instance.
    /// </summary>
    /// <param name="group">The group asset.</param>
    /// <param name="indexCount">The quad mesh's index count.</param>
    /// <returns>The filled record.</returns>
    public static EmitterParams3D FromAsset(ParticleGroup3DAsset group, uint indexCount)
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
            0f);
        parameters.Life = new Vector4(
            group.Lifetime.Min,
            group.Lifetime.Max,
            Math.Clamp(group.FadeIn, 0f, 1f),
            Math.Clamp(group.FadeOut, 0f, 1f));
        parameters.Size = new Vector4(group.Size.Min, group.Size.Max, group.EndScale, 0f);
        parameters.Rotation = new Vector4(
            group.StartRotation.Min,
            group.StartRotation.Max,
            group.AngularVelocity.Min,
            group.AngularVelocity.Max);
        parameters.Motion = new Vector4(group.Gravity, group.Drag);
        parameters.ColorMin = group.StartColor.Min;
        parameters.ColorMax = group.StartColor.Max;
        parameters.ColorEnd = group.EndColor;
        parameters.Tint = group.Material.Tint;
        ParticleFlipbook? flipbook = group.Material.Flipbook;
        parameters.Flipbook = flipbook != null
            ? new Vector4(flipbook.Rows, flipbook.Cols, flipbook.Fps, flipbook.Loop ? 1f : 0f)
            : new Vector4(1f, 1f, 0f, 0f);
        parameters.IndexCount = indexCount;
        parameters.Flags = group.SimulationSpace == ParticleSimulationSpace.World ? FlagWorldSpace : 0u;
        return parameters;
    }
}
