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
    /// <summary>
    /// The per-group runtime state of a 2D effect instance: one value record in
    /// the instance's group array — a struct so an instance's groups are one
    /// contiguous allocation instead of one object per group. Mutate array
    /// elements through <c>ref</c> locals only; copies taken elsewhere (the draw
    /// plan's bucket lists) are value snapshots.
    /// </summary>
    internal struct GroupState
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
        public bool Active;

        /// <summary>
        /// Whether the group draws and simulates (per-instance state; the asset is
        /// never mutated). A hidden group drops out of the draw plan — its live
        /// particles freeze — while its emission timeline keeps advancing,
        /// mirroring <see cref="ParticleEffectInstance2D.IsVisible"/>.
        /// </summary>
        public bool Visible;

        /// <summary>The group's render material (shared per group asset).</summary>
        public required GraphicsMaterial Material;

        /// <summary>
        /// The group's behavior emit compute material (shared per behavior library,
        /// resolved once at creation so the frame loop needs no cache lookup).
        /// </summary>
        public required ComputeMaterial EmitMaterial;

        /// <summary>
        /// The group's behavior simulate compute material (shared per behavior
        /// library, resolved once at creation so the frame loop needs no cache
        /// lookup).
        /// </summary>
        public required ComputeMaterial SimulateMaterial;

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
        float height,
        int seed,
        GroupState[] groups)
    {
        _system = system;
        Asset = asset;
        Transform = transform;
        Height = height;
        _random = new FastRandom(unchecked((uint)(seed == 0 ? Environment.TickCount : seed)));
        _groups = groups;
        IsPlaying = true;
    }

    /// <summary>The effect asset this instance plays.</summary>
    public ParticleEffect2DAsset Asset { get; }

    /// <summary>The emitter transform of the whole effect (all groups).</summary>
    public Transform2D Transform { get; set; }

    /// <summary>
    /// The emitter's height above its ground-plane <see cref="Transform"/>. World-space
    /// particles capture it at spawn; local-space particles follow it while alive.
    /// </summary>
    public float Height { get; set; }

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

    /// <summary>Whether a group is visible (drawn and simulated) on this instance.</summary>
    /// <param name="groupIndex">The group index (0 .. <see cref="GroupCount"/> - 1).</param>
    public bool IsGroupVisible(int groupIndex) => _groups[groupIndex].Visible;

    /// <summary>
    /// Shows or hides a group for this instance (the asset is not mutated). A
    /// hidden group drops out of the draw plan — its emit/simulate dispatches and
    /// draw are skipped, so its live particles freeze — while its emission
    /// timeline keeps advancing, mirroring <see cref="IsVisible"/>.
    /// </summary>
    /// <param name="groupIndex">The group index (0 .. <see cref="GroupCount"/> - 1).</param>
    /// <param name="visible">True to draw and simulate the group.</param>
    public void SetGroupVisible(int groupIndex, bool visible)
    {
        _groups[groupIndex].Visible = visible;
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
        ref GroupState group = ref _groups[groupIndex];
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
            ref GroupState group = ref _groups[i];
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
            ref GroupState group = ref _groups[i];
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
            parameters.EmitterHeight = Height;
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
