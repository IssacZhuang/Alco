using Alco;
using Alco.Rendering;

namespace Alco.Particles;

/// <summary>
/// A live instance of a 2D particle effect asset: owns the per-group runtime state
/// (pool slice, emitter slot, emission timeline) and the emitter transform. Created
/// through <see cref="GpuParticleSystem2D.CreateInstance"/>; disposing it returns its
/// slices and slots to the shared pool — cheap enough for effects that spawn and
/// despawn constantly.
/// </summary>
public sealed class ParticleEffectInstance2D : AutoDisposable
{
    /// <summary>The per-group runtime state of a 2D effect instance.</summary>
    internal sealed class GroupState
    {
        /// <summary>The group asset.</summary>
        public required ParticleGroup2DAsset Asset;

        /// <summary>The emitter slot (params/draw-args index).</summary>
        public uint Slot;

        /// <summary>The pool slice.</summary>
        public ParticleSlice Slice;

        /// <summary>The ring-buffer write cursor.</summary>
        public uint Cursor;

        /// <summary>The unwrapped emission timeline position.</summary>
        public float Time;

        /// <summary>The fractional-particle accumulator.</summary>
        public float Accumulator;

        /// <summary>The time since emission stopped (drives deactivation).</summary>
        public float IdleTimer;

        /// <summary>The spawn count of the current frame.</summary>
        public uint SpawnCount;

        /// <summary>Whether the group still does GPU work (emitting or has live particles).</summary>
        public bool Active = true;

        /// <summary>The group's render material (shared per group asset).</summary>
        public required GraphicsMaterial Material;

        /// <summary>
        /// The live emission rate in particles per second (per-instance; initialized
        /// from the asset, editable through <see cref="SetGroupEmissionRate"/>).
        /// </summary>
        public float EmissionRate;

        /// <summary>
        /// The live lifetime range in seconds (per-instance; initialized from the
        /// asset, synchronized with the GPU record by <see cref="SetGroupParams"/>).
        /// Drives the deactivation timeout once emission ends (stopped, or a
        /// finished one-shot timeline).
        /// </summary>
        public ParticleRange Lifetime;
    }

    private readonly GpuParticleSystem2D _system;
    private readonly GroupState[] _groups;
    private FastRandom _random;

    internal ParticleEffectInstance2D(
        GpuParticleSystem2D system,
        ParticleEffect2DAsset asset,
        in Transform2D transform,
        int seed,
        GroupState[] groups)
    {
        _system = system;
        Asset = asset;
        Transform = transform;
        _random = new FastRandom(unchecked((uint)(seed == 0 ? Environment.TickCount : seed)));
        _groups = groups;
        IsPlaying = true;
    }

    /// <summary>The effect asset this instance plays.</summary>
    public ParticleEffect2DAsset Asset { get; }

    /// <summary>The emitter transform of the whole effect (all groups).</summary>
    public Transform2D Transform { get; set; }

    /// <summary>Whether the effect is emitting (starts playing on creation).</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>Whether the instance still does GPU work (emitting or particles alive).</summary>
    public bool IsActive { get; private set; } = true;

    /// <summary>
    /// Whether the instance is visible to the camera (culling hook driven by the
    /// caller, e.g. a spatial grid). An invisible instance skips its emit/simulate
    /// dispatches and its draw — GPU cost drops to zero — while its emission
    /// timeline keeps advancing, so a looping effect resumes seamlessly instead of
    /// bursting when it becomes visible again.
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>The unwrapped emission timeline position in seconds.</summary>
    public float Time => _groups.Length > 0 ? _groups[0].Time : 0f;

    /// <summary>The group states (diagnostics).</summary>
    internal GroupState[] Groups => _groups;

    /// <summary>The number of emitter groups of the effect.</summary>
    public int GroupCount => _groups.Length;

    /// <summary>The name of one of the instance's emitter groups.</summary>
    /// <param name="groupIndex">The group index (0 .. <see cref="GroupCount"/> - 1).</param>
    /// <returns>The asset name of the group.</returns>
    public string GetGroupName(int groupIndex) => _groups[groupIndex].Asset.Name;

    /// <summary>The live emission rate of a group, in particles per second.</summary>
    /// <param name="groupIndex">The group index (0 .. <see cref="GroupCount"/> - 1).</param>
    /// <returns>The per-instance emission rate.</returns>
    public float GetGroupEmissionRate(int groupIndex) => _groups[groupIndex].EmissionRate;

    /// <summary>
    /// Overrides the emission rate of a group for this instance (the asset is not
    /// mutated); applies to future spawns.
    /// </summary>
    /// <param name="groupIndex">The group index (0 .. <see cref="GroupCount"/> - 1).</param>
    /// <param name="rate">The new rate in particles per second (clamped to &gt;= 0).</param>
    public void SetGroupEmissionRate(int groupIndex, float rate)
    {
        _groups[groupIndex].EmissionRate = Math.Max(rate, 0f);
    }

    /// <summary>A copy of the group's live parameter record (static and per-frame fields).</summary>
    /// <param name="groupIndex">The group index (0 .. <see cref="GroupCount"/> - 1).</param>
    /// <returns>The slot's current record.</returns>
    public EmitterParams2D GetGroupParams(int groupIndex) => _system.ParamsRef(_groups[groupIndex].Slot);

    /// <summary>
    /// Replaces the static (asset-authored) parameter fields of a group — speed,
    /// lifetime, size, gravity, tint, the stretch and lookup flags — for this
    /// instance only, without respawning it; the slot-bound and per-frame fields
    /// keep their live values (see <see cref="EmitterParams2D.MergeEdited"/>) and
    /// the asset is not mutated. The group's CPU-side lifetime follows
    /// <see cref="EmitterParams2D.Life"/>.X/Y, so a stopped group still deactivates
    /// on time. The upload rides the regular dirty-range path: active groups upload
    /// the same frame, dormant groups when they reactivate.
    /// </summary>
    /// <param name="groupIndex">The group index (0 .. <see cref="GroupCount"/> - 1).</param>
    /// <param name="parameters">The record carrying the edited static fields.</param>
    public void SetGroupParams(int groupIndex, in EmitterParams2D parameters)
    {
        GroupState group = _groups[groupIndex];
        EmitterParams2D merged = EmitterParams2D.MergeEdited(_system.ParamsRef(group.Slot), parameters);
        _system.ParamsRef(group.Slot) = merged;
        group.Lifetime = new ParticleRange(merged.Life.X, merged.Life.Y);
    }

    /// <summary>Resumes emission.</summary>
    public void Play()
    {
        IsPlaying = true;
        for (int i = 0; i < _groups.Length; i++)
        {
            _groups[i].IdleTimer = 0f;
            _groups[i].Active = true;
        }
        IsActive = true;
    }

    /// <summary>Stops emission; the live particles die out naturally.</summary>
    public void Stop()
    {
        IsPlaying = false;
    }

    /// <summary>Kills every live particle and restarts the emission timeline.</summary>
    public void Restart()
    {
        for (int i = 0; i < _groups.Length; i++)
        {
            GroupState group = _groups[i];
            _system.Pool.QueueKill(group.Slice);
            group.Cursor = 0;
            group.Time = 0f;
            group.Accumulator = 0f;
            group.IdleTimer = 0f;
            group.Active = true;
        }
        IsPlaying = true;
        IsActive = true;
    }

    /// <summary>
    /// Advances every group's emission timeline by one frame and writes the
    /// per-frame parameter fields into the pool's CPU mirror.
    /// </summary>
    internal void AdvanceFrame(float deltaTime, ref uint dirtyMin, ref uint dirtyMax)
    {
        bool anyActive = false;
        for (int i = 0; i < _groups.Length; i++)
        {
            GroupState group = _groups[i];
            ParticleGroup2DAsset asset = group.Asset;
            // The lifecycle step gates emission per group: a one-shot group whose
            // timeline ran out idles and deactivates even while the instance still
            // plays, so finished effects are destroyable (IsActive -> false).
            group.SpawnCount = ParticleEmission.AdvanceLifecycle(
                ref group.Time,
                ref group.Accumulator,
                ref group.IdleTimer,
                deltaTime,
                IsPlaying,
                group.EmissionRate,
                asset.Duration,
                asset.Looping,
                asset.Bursts,
                ref _random,
                group.Slice.Capacity,
                group.Lifetime.Max,
                out bool active);
            group.Active = active;
            if (!group.Active)
            {
                continue;
            }
            anyActive = true;

            ref EmitterParams2D parameters = ref _system.ParamsRef(group.Slot);
            parameters.SpawnCount = group.SpawnCount;
            parameters.EmitCursor = group.Cursor;
            parameters.DeltaTime = deltaTime;
            parameters.EmitterTime = asset.Duration > 0f ? group.Time % asset.Duration : group.Time;
            parameters.FrameSeed = _random.NextUint();
            parameters.WorldMatrix = Transform.Matrix;
            group.Cursor = (group.Cursor + group.SpawnCount) % group.Slice.Capacity;
            dirtyMin = Math.Min(dirtyMin, group.Slot);
            dirtyMax = Math.Max(dirtyMax, group.Slot);
        }
        IsActive = anyActive;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _system.ReleaseInstance(this);
        }
    }
}
