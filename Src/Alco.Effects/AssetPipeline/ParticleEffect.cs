using System.Text.Json.Serialization;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Effects;

/// <summary>
/// A particle effect asset (<c>.afx</c>): the data-only, serializable description
/// of an effect as a list of emitter groups, each with its own simulation and
/// material configuration. 2D and 3D effects are separate types with non-mixing
/// data structures.
/// </summary>
public abstract class ParticleEffect : IJsonOnDeserialized
{
    /// <summary>Format version of the particle effect files this runtime consumes.</summary>
    public const string FormatVersion = "1.0";

    /// <summary>The format version the file declares; null when constructed in code.</summary>
    public string? Version { get; set; }

    /// <summary>The effect name; the loader defaults it to the source file name when omitted.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Normalize the deserialized content (trimmed name, no null entries).</summary>
    public virtual void OnDeserialized()
    {
        Name = Name.Trim();
    }
}

/// <summary>A 2D particle effect: a list of 2D emitter groups.</summary>
public sealed class ParticleEffect2D : ParticleEffect
{
    private List<ParticleGroup2D> _groups = [];

    /// <summary>The emitter groups of the effect; rendered in list order.</summary>
    public List<ParticleGroup2D> Groups
    {
        get => _groups;
        set => _groups = value ?? [];
    }

    /// <inheritdoc />
    public override void OnDeserialized()
    {
        base.OnDeserialized();
        _groups.RemoveAll(group => group == null!);
    }
}

/// <summary>A 3D particle effect: a list of 3D emitter groups.</summary>
public sealed class ParticleEffect3D : ParticleEffect
{
    private List<ParticleGroup3D> _groups = [];

    /// <summary>The emitter groups of the effect; rendered in list order.</summary>
    public List<ParticleGroup3D> Groups
    {
        get => _groups;
        set => _groups = value ?? [];
    }

    /// <inheritdoc />
    public override void OnDeserialized()
    {
        base.OnDeserialized();
        _groups.RemoveAll(group => group == null!);
    }
}

/// <summary>
/// One emitter of a particle effect: emission rate and bursts, lifetime, size,
/// rotation, color and drag ranges, the simulation space, the optional slang
/// behavior module and the material configuration. Dimension-specific shape and
/// direction parameters live on the derived 2D/3D types.
/// </summary>
public abstract class ParticleGroup
{
    /// <summary>The group name (diagnostics only).</summary>
    public string Name { get; set; } = "Group";

    /// <summary>
    /// The maximum number of particles alive at once in this group. The pool slice
    /// of the group is allocated at this capacity; excess spawns overwrite the
    /// oldest particles (ring buffer semantics).
    /// </summary>
    public int MaxParticles { get; set; } = 4096;

    /// <summary>
    /// The length of the emission timeline in seconds; 0 means the emitter never
    /// stops emitting on its own (infinite duration).
    /// </summary>
    public float Duration { get; set; }

    /// <summary>Whether the timeline wraps (and bursts re-fire) when reaching <see cref="Duration"/>.</summary>
    public bool Looping { get; set; } = true;

    /// <summary>The continuous emission rate in particles per second.</summary>
    public float EmissionRate { get; set; } = 100f;

    /// <summary>One-shot bursts fired at points of the emission timeline.</summary>
    public List<ParticleBurst> Bursts { get; set; } = [];

    /// <summary>The particle lifetime range in seconds.</summary>
    public ParticleRange Lifetime { get; set; } = new(1f, 2f);

    /// <summary>
    /// The initial speed range in world units per second. Defaults to 0
    /// (stationary): an omitted speed must not silently combine with the
    /// default constant direction into motion the author never wrote —
    /// authors opt into movement by writing a speed.
    /// </summary>
    public ParticleRange Speed { get; set; } = new();

    /// <summary>
    /// The per-particle color at spawn, sampled component-wise between min (rgba)
    /// and max. Alpha is the opacity (1 = opaque).
    /// </summary>
    public ParticleColorRange StartColor { get; set; } = new(ColorFloat.White);

    /// <summary>The color the particle lerps to at the end of its life.</summary>
    public ColorFloat EndColor { get; set; } = ColorFloat.Transparent;

    /// <summary>
    /// The normalized lifetime fraction over which the particle fades in
    /// (multiplies alpha by a smooth ramp), e.g. 0.1 = first 10% of the life.
    /// </summary>
    public float FadeIn { get; set; }

    /// <summary>
    /// The normalized lifetime fraction at the end over which the particle fades
    /// out, e.g. 0.5 = the alpha ramps to the end color's alpha over the last half
    /// of the life.
    /// </summary>
    public float FadeOut { get; set; } = 0.5f;

    /// <summary>The scale multiplier the particle lerps to at the end of its life (1 = constant size).</summary>
    public float EndScale { get; set; } = 1f;

    /// <summary>
    /// The color gradient over the particle's life, keyed by normalized age (0..1).
    /// When non-empty it replaces the <see cref="StartColor"/> → <see cref="EndColor"/>
    /// lerp and multiplies the spawn color; <see cref="FadeIn"/>/<see cref="FadeOut"/>
    /// still apply on top.
    /// </summary>
    public List<ParticleColorKey>? ColorGradient { get; set; }

    /// <summary>
    /// The size multiplier curve over the particle's life, keyed by normalized age
    /// (0..1); when non-empty it replaces the <see cref="EndScale"/> lerp by
    /// multiplying the spawn size with the curve sample.
    /// </summary>
    public List<ParticleScalarKey>? SizeCurve { get; set; }

    /// <summary>
    /// The linear drag coefficient: velocity decays as <c>v *= exp(-drag * dt)</c>.
    /// </summary>
    public float Drag { get; set; }

    /// <summary>The space the group's particles simulate in.</summary>
    public ParticleSimulationSpace SimulationSpace { get; set; } = ParticleSimulationSpace.World;

    /// <summary>
    /// The slang shader library module defining this group's simulation behavior:
    /// exactly one struct implementing <c>IParticleBehavior2D</c> (2D effects) or
    /// <c>IParticleBehavior3D</c> (3D effects); null selects the built-in default
    /// behavior.
    /// </summary>
    public ShaderLibrary? Behavior { get; set; }

    /// <summary>
    /// The material asset (<c>.amat</c>) defining the group's visuals: its surface
    /// module shades the particle fragments and may adjust their vertices, and it
    /// carries shared textures and uniform parameters. Null uses the engine's default
    /// particle surface.
    /// </summary>
    public MaterialAsset? Material { get; set; }

    /// <summary>
    /// The particle texture: overrides the material's <c>texture</c> slot on the
    /// group's own material instance (the .amat provides the shared resources, this
    /// provides the per-group sprite — material-instance derivation). Null keeps the
    /// material's own binding (or its fallback).
    /// </summary>
    public Texture2D? Texture { get; set; }

    /// <summary>
    /// The blend state preset name (<c>"AlphaBlend"</c>, <c>"Additive"</c>, …);
    /// null defaults to <see cref="BlendState.AlphaBlend"/>. Additive blending is
    /// order-independent and recommended for unsorted GPU particles.
    /// </summary>
    public BlendState? Blend { get; set; }

    /// <summary>
    /// The depth-stencil state preset of the group's material (<c>"None"</c>,
    /// <c>"Read"</c>, <c>"Write"</c>, …); null keeps the particle pass's default.
    /// A 2D group authoring a depth state also opts into the pass's depth-base world z.
    /// </summary>
    public DepthStencilState? Depth { get; set; }

    /// <summary>A global color multiplier applied on top of the per-particle color.</summary>
    public ColorFloat Tint { get; set; } = ColorFloat.White;

    /// <summary>Flipbook animation of the particle texture; null disables it.</summary>
    public ParticleFlipbook? Flipbook { get; set; }
}

/// <summary>A 2D emitter group; adds 2D shape, direction, rotation and size parameters.</summary>
public sealed class ParticleGroup2D : ParticleGroup
{
    /// <summary>The emission shape.</summary>
    public ParticleShape2D Shape { get; set; } = new();

    /// <summary>
    /// The fixed spawn offset in emitter-local space: every particle is born
    /// this far from the emitter origin (rotated with the emitter), on top of
    /// the shape sample. Offsets a directional effect away from its anchor —
    /// e.g. a muzzle flash born just past the muzzle point.
    /// </summary>
    public System.Numerics.Vector2 PositionOffset { get; set; }

    /// <summary>The base emission direction (used by <see cref="ParticleDirectionMode.Constant"/>).</summary>
    public System.Numerics.Vector2 Direction { get; set; } = new(0f, 1f);

    /// <summary>
    /// How the initial direction is chosen; <see cref="ParticleDirectionMode.Radial"/>
    /// emits outward through the spawn position (e.g. explosions).
    /// </summary>
    public ParticleDirectionMode DirectionMode { get; set; } = ParticleDirectionMode.Constant;

    /// <summary>
    /// The direction randomization half-angle in radians: the base direction is
    /// rotated by a uniform sample in [-spread, +spread].
    /// </summary>
    public float SpreadAngle { get; set; }

    /// <summary>The initial rotation range in radians.</summary>
    public ParticleRange StartRotation { get; set; }

    /// <summary>The angular velocity range in radians per second.</summary>
    public ParticleRange AngularVelocity { get; set; }

    /// <summary>Whether the initial rotation aligns to the velocity direction (e.g. sparks).</summary>
    public bool AlignRotationToVelocity { get; set; }

    /// <summary>
    /// Whether the quad stretches along its velocity (requires
    /// <see cref="AlignRotationToVelocity"/>): the velocity-axis extent becomes
    /// base size × <see cref="StretchLengthScale"/> + speed ×
    /// <see cref="StretchSpeedScale"/>, while the perpendicular extent stays the
    /// base size.
    /// </summary>
    public bool VelocityStretch { get; set; }

    /// <summary>The base-size multiplier along the velocity axis (<see cref="VelocityStretch"/>).</summary>
    public float StretchLengthScale { get; set; } = 1f;

    /// <summary>The extra length per unit of speed along the velocity axis (<see cref="VelocityStretch"/>).</summary>
    public float StretchSpeedScale { get; set; } = 0.05f;

    /// <summary>The initial size (quad extents) range in world units.</summary>
    public ParticleVector2Range Size { get; set; } = new(new System.Numerics.Vector2(2f));

    /// <summary>The constant gravity acceleration in world units per second squared.</summary>
    public System.Numerics.Vector2 Gravity { get; set; }

    /// <summary>
    /// The particle's initial height above the emitter's ground plane. Height is
    /// projected along positive world Y by the 2D render pass while remaining
    /// independent from the particle's ground-plane <see cref="Shape"/> position.
    /// </summary>
    public ParticleRange StartHeight { get; set; }

    /// <summary>The initial height velocity range in world units per second.</summary>
    public ParticleRange HeightVelocity { get; set; }

    /// <summary>
    /// The constant height acceleration in world units per second squared. Positive
    /// values rise; negative values fall. <see cref="ParticleGroup.Drag"/>
    /// damps both ground-plane and height velocity.
    /// </summary>
    public float HeightAcceleration { get; set; }
}

/// <summary>A 3D emitter group; adds 3D shape, direction, billboard roll and size parameters.</summary>
public sealed class ParticleGroup3D : ParticleGroup
{
    /// <summary>The emission shape.</summary>
    public ParticleShape3D Shape { get; set; } = new();

    /// <summary>
    /// The fixed spawn offset in emitter-local space: every particle is born
    /// this far from the emitter origin (rotated with the emitter), on top of
    /// the shape sample. Offsets a directional effect away from its anchor —
    /// e.g. a muzzle flash born just past the muzzle point.
    /// </summary>
    public System.Numerics.Vector3 PositionOffset { get; set; }

    /// <summary>The base emission direction (used by <see cref="ParticleDirectionMode.Constant"/>).</summary>
    public System.Numerics.Vector3 Direction { get; set; } = new(0f, 0f, 1f);

    /// <summary>How the initial direction is chosen.</summary>
    public ParticleDirectionMode DirectionMode { get; set; } = ParticleDirectionMode.Constant;

    /// <summary>
    /// The cone half-angle in radians the base direction is randomized within
    /// (<see cref="ParticleDirectionMode.Constant"/>).
    /// </summary>
    public float SpreadAngle { get; set; }

    /// <summary>The initial billboard roll (screen-space rotation) range in radians.</summary>
    public ParticleRange StartRotation { get; set; }

    /// <summary>The billboard roll velocity range in radians per second.</summary>
    public ParticleRange AngularVelocity { get; set; }

    /// <summary>The initial uniform size range in world units.</summary>
    public ParticleRange Size { get; set; } = new(0.5f, 1f);

    /// <summary>
    /// Whether the billboard stretches along the screen-space projection of its
    /// velocity: the velocity-axis extent becomes base size ×
    /// <see cref="StretchLengthScale"/> + speed × <see cref="StretchSpeedScale"/>,
    /// while the perpendicular extent stays the base size and the roll is ignored.
    /// </summary>
    public bool VelocityStretch { get; set; }

    /// <summary>The base-size multiplier along the velocity axis (<see cref="VelocityStretch"/>).</summary>
    public float StretchLengthScale { get; set; } = 1f;

    /// <summary>The extra length per unit of speed along the velocity axis (<see cref="VelocityStretch"/>).</summary>
    public float StretchSpeedScale { get; set; } = 0.05f;

    /// <summary>The constant gravity acceleration in world units per second squared.</summary>
    public System.Numerics.Vector3 Gravity { get; set; }
}

/// <summary>
/// A <see cref="ColorFloat"/> range sampled component-wise at spawn time (on the
/// GPU); used for spawn colors. Serialized as
/// <c>{ "min": "#RRGGBBAA", "max": {...} }</c> — each bound accepts every shape
/// the particle color converter accepts (number, hex color, component object).
/// </summary>
public struct ParticleColorRange
{
    /// <summary>The inclusive lower bound of the sampled range.</summary>
    public ColorFloat Min { get; set; }

    /// <summary>The inclusive upper bound of the sampled range.</summary>
    public ColorFloat Max { get; set; }

    /// <summary>Creates a range that always samples <paramref name="value"/>.</summary>
    public ParticleColorRange(ColorFloat value)
    {
        Min = value;
        Max = value;
    }
}
