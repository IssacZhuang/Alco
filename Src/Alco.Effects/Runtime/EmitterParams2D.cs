using System.Numerics;
using System.Runtime.InteropServices;

namespace Alco.Effects;

/// <summary>
/// The per-emitter parameter record of a 2D particle group, written by the CPU every frame and read by the emit/simulate/render shaders.
/// The layout must match the slang <c>EmitterParams2D</c> struct (368 bytes, row-major matrices).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EmitterParams2D
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
    /// Packed control flags (world-space simulation, gradients, size curves, velocity stretch, reversed flipbook, depth base).
    /// </summary>
    public uint Flags;

    /// <summary>The emitter's row-major transform matrix (used at spawn in world space, at draw in local space).</summary>
    public Matrix4x4 WorldMatrix;

    /// <summary>Emission shape: x = type (0 point, 1 circle, 2 box), y = radius, z = inner radius fraction.</summary>
    public Vector4 Shape;

    /// <summary>Box half extents (xy).</summary>
    public Vector4 Extents;

    /// <summary>Emission direction: xy = base direction, z = spread half-angle (rad), w = direction mode (0 constant, 1 radial).</summary>
    public Vector4 Emission;

    /// <summary>Speed: x = min, y = max, z = align rotation to velocity (0/1), w = velocity-stretch speed scale.</summary>
    public Vector4 Speed;

    /// <summary>Life: x = lifetime min, y = lifetime max, z = fade-in fraction, w = fade-out fraction.</summary>
    public Vector4 Life;

    /// <summary>Spawn size: xy = min extents, zw = max extents.</summary>
    public Vector4 Size;

    /// <summary>Rotation: x = start rotation min, y = max, z = angular velocity min, w = max.</summary>
    public Vector4 Rotation;

    /// <summary>Over-life: x = end scale multiplier, y = velocity-stretch length scale.</summary>
    public Vector4 OverLife;

    /// <summary>Motion: xy = planar gravity, z = drag, w = height acceleration.</summary>
    public Vector4 Motion;

    /// <summary>Height motion: x/y = initial height min/max, z/w = initial height velocity min/max.</summary>
    public Vector4 HeightMotion;

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

    /// <summary>Bindless sampled descriptor index of this emitter's sprite texture.</summary>
    public uint SpriteTextureId;

    /// <summary>A custom per-instance float channel surfaced as <c>ParticleVertexInput.customData</c>.</summary>
    public float CustomData;

    /// <summary>The emitter's height above its ground-plane transform.</summary>
    public float EmitterHeight;

    /// <summary>The group's base world z for scene-depth arbitration (2D only).</summary>
    public float DepthBase;

    /// <summary>Fixed spawn offset in emitter-local space.</summary>
    public Vector2 PositionOffset;

    /// <summary>Bindless sampled descriptor index of this emitter's 256x1 color gradient texture.</summary>
    public uint ColorGradientTextureId;

    /// <summary>Bindless sampled descriptor index of this emitter's 256x1 size curve texture.</summary>
    public uint SizeCurveTextureId;

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

    /// <summary>The depth-base flag bit of <see cref="Flags"/> (2D only).</summary>
    public const uint FlagDepthBase = 32u;

    /// <summary>
    /// Merges an edited record into the live slot record, preserving slot-bound, per-frame, and bindless texture ID fields.
    /// </summary>
    internal static EmitterParams2D MergeEdited(in EmitterParams2D live, in EmitterParams2D edited)
    {
        EmitterParams2D merged = edited;
        merged.SpawnCount = live.SpawnCount;
        merged.EmitCursor = live.EmitCursor;
        merged.Capacity = live.Capacity;
        merged.SliceOffset = live.SliceOffset;
        merged.DeltaTime = live.DeltaTime;
        merged.EmitterTime = live.EmitterTime;
        merged.FrameSeed = live.FrameSeed;
        merged.WorldMatrix = live.WorldMatrix;
        merged.EmitterHeight = live.EmitterHeight;
        merged.DepthBase = live.DepthBase;
        merged.SpriteTextureId = live.SpriteTextureId;
        merged.ColorGradientTextureId = live.ColorGradientTextureId;
        merged.SizeCurveTextureId = live.SizeCurveTextureId;
        return merged;
    }

    /// <summary>
    /// Fills the static (asset-authored) fields of the record from a group asset;
    /// bindless descriptor IDs and per-frame fields are filled by the system and instance.
    /// </summary>
    /// <param name="group">The group asset.</param>
    /// <returns>The filled record.</returns>
    public static EmitterParams2D FromAsset(ParticleGroup2D group)
    {
        ArgumentNullException.ThrowIfNull(group);
        EmitterParams2D parameters = default;
        parameters.WorldMatrix = Matrix4x4.Identity;
        parameters.Shape = new Vector4(
            (float)group.Shape.Type,
            group.Shape.Radius,
            Math.Clamp(group.Shape.InnerRadius, 0f, 1f),
            0f);
        parameters.Extents = new Vector4(group.Shape.Extents, 0f, 0f);
        parameters.Emission = new Vector4(
            group.Direction,
            group.SpreadAngle,
            (float)group.DirectionMode);
        parameters.Speed = new Vector4(
            group.Speed.Min,
            group.Speed.Max,
            group.AlignRotationToVelocity ? 1f : 0f,
            group.StretchSpeedScale);
        parameters.Life = new Vector4(
            group.Lifetime.Min,
            group.Lifetime.Max,
            Math.Clamp(group.FadeIn, 0f, 1f),
            Math.Clamp(group.FadeOut, 0f, 1f));
        parameters.Size = new Vector4(group.Size.Min.X, group.Size.Min.Y, group.Size.Max.X, group.Size.Max.Y);
        parameters.Rotation = new Vector4(
            group.StartRotation.Min,
            group.StartRotation.Max,
            group.AngularVelocity.Min,
            group.AngularVelocity.Max);
        parameters.OverLife = new Vector4(group.EndScale, group.StretchLengthScale, 0f, 0f);
        parameters.Motion = new Vector4(group.Gravity, group.Drag, group.HeightAcceleration);
        parameters.HeightMotion = new Vector4(
            group.StartHeight.Min,
            group.StartHeight.Max,
            group.HeightVelocity.Min,
            group.HeightVelocity.Max);
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
        parameters.PositionOffset = group.PositionOffset;
        parameters.Flags = group.SimulationSpace == ParticleSimulationSpace.World ? FlagWorldSpace : 0u;
        if (group.ColorGradient is { Count: > 0 })
            parameters.Flags |= FlagColorGradient;
        if (group.SizeCurve is { Count: > 0 })
            parameters.Flags |= FlagSizeCurve;
        if (group.VelocityStretch && group.AlignRotationToVelocity)
            parameters.Flags |= FlagVelocityStretch;
        if (flipbook is { Reverse: true })
            parameters.Flags |= FlagFlipbookReverse;
        if (group.Depth is not null)
            parameters.Flags |= FlagDepthBase;
        return parameters;
    }
}
