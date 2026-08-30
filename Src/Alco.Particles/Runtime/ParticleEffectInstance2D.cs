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

    /// <summary>The unwrapped emission timeline position in seconds.</summary>
    public float Time => _groups.Length > 0 ? _groups[0].Time : 0f;

    /// <summary>The group states (diagnostics).</summary>
    internal GroupState[] Groups => _groups;

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
            group.SpawnCount = 0;
            if (IsPlaying)
            {
                group.SpawnCount = ParticleEmission.Advance(
                    ref group.Time,
                    ref group.Accumulator,
                    deltaTime,
                    asset.EmissionRate,
                    asset.Duration,
                    asset.Looping,
                    asset.Bursts,
                    ref _random,
                    group.Slice.Capacity);
                group.IdleTimer = 0f;
            }
            else
            {
                group.IdleTimer += deltaTime;
            }
            // A stopped group deactivates once every particle had time to die.
            group.Active = IsPlaying || group.IdleTimer <= asset.Lifetime.Max + 0.1f;
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
