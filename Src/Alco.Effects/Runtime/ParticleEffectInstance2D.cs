using Alco.Graphics;

namespace Alco.Effects;

/// <summary>
/// A live instance of a 2D particle effect asset owning per-group runtime state (pool slice,
/// emitter slot, emission timeline) and emitter transform. Created through
/// <see cref="GpuParticleSystem2D.CreateInstance"/>; disposal returns slices and slots to the pool.
/// Property updates and lifecycle controls are thread-safe.
/// </summary>
public sealed class ParticleEffectInstance2D : AutoDisposable
{
    /// <summary>Per-group runtime state stored by value in the instance's group array.</summary>
    internal struct GroupState
    {
        public required ParticleGroup2D Asset;
        public uint Slot;
        public ParticleSlice Slice;
        public uint Cursor;
        public float Time;
        public float Accumulator;
        public float IdleTimer;
        public uint SpawnCount;
        public bool Active;
        public bool Visible;
        public required Material Material;
        public required RootField<GpuParticleSystem2D.ParticlePassData2D> PassField;
        public required DepthStencilState DepthStencil;
        public required Shader EmitShader;
        public required Shader SimulateShader;
        public float EmissionRate;
        public ParticleRange Lifetime;
    }

    private readonly GpuParticleSystem2D _system;
    private readonly GroupState[] _groups;
    private readonly Lock _propLock = new();
    private FastRandom _random;
    private Transform2D _transform;
    private float _height;
    private float _depthBase;
    private bool _isPlaying;
    private bool _isActive = true;
    private bool _isVisible = true;

    internal ParticleEffectInstance2D(
        GpuParticleSystem2D system,
        ParticleEffect2D asset,
        in Transform2D transform,
        float height,
        int seed,
        GroupState[] groups)
    {
        _system = system;
        Asset = asset;
        _transform = transform;
        _height = height;
        _random = new FastRandom(unchecked((uint)(seed == 0 ? Environment.TickCount : seed)));
        _groups = groups;
        _isPlaying = true;
    }

    /// <summary>The effect asset this instance plays.</summary>
    public ParticleEffect2D Asset { get; }

    /// <summary>The emitter transform applied to every group of the effect.</summary>
    public Transform2D Transform
    {
        get { using Lock.Scope _ = _propLock.EnterScope(); return _transform; }
        set { using Lock.Scope _ = _propLock.EnterScope(); _transform = value; }
    }

    /// <summary>The emitter's height above its ground-plane transform.</summary>
    public float Height
    {
        get => Volatile.Read(ref _height);
        set => Volatile.Write(ref _height, value);
    }

    /// <summary>The base world z written into <see cref="EmitterParams2D.DepthBase"/> when depth testing is enabled.</summary>
    public float DepthBase
    {
        get => Volatile.Read(ref _depthBase);
        set => Volatile.Write(ref _depthBase, value);
    }

    /// <summary>Whether the effect is currently emitting.</summary>
    public bool IsPlaying
    {
        get => Volatile.Read(ref _isPlaying);
        private set => Volatile.Write(ref _isPlaying, value);
    }

    /// <summary>Whether the instance still does GPU work (emitting or particles alive).</summary>
    public bool IsActive
    {
        get => Volatile.Read(ref _isActive);
        private set => Volatile.Write(ref _isActive, value);
    }

    /// <summary>Whether the instance is visible for simulation and rendering.</summary>
    public bool IsVisible
    {
        get => Volatile.Read(ref _isVisible);
        set
        {
            if (Volatile.Read(ref _isVisible) == value)
                return;
            Volatile.Write(ref _isVisible, value);
            _system.InvalidatePlan();
        }
    }

    /// <summary>The unwrapped emission timeline position in seconds.</summary>
    public float Time => _groups.Length > 0 ? Volatile.Read(ref _groups[0].Time) : 0f;

    /// <summary>The group states (diagnostics).</summary>
    internal GroupState[] Groups => _groups;

    /// <summary>The number of emitter groups of the effect.</summary>
    public int GroupCount => _groups.Length;

    /// <summary>Gets the authored name of the group at <paramref name="groupIndex"/>.</summary>
    public string GetGroupName(int groupIndex) => _groups[groupIndex].Asset.Name;

    /// <summary>Gets the live emission rate of a group in particles per second.</summary>
    public float GetGroupEmissionRate(int groupIndex) => Volatile.Read(ref _groups[groupIndex].EmissionRate);

    /// <summary>Overrides the emission rate of a group for future spawns without mutating the asset.</summary>
    public void SetGroupEmissionRate(int groupIndex, float rate)
    {
        Volatile.Write(ref _groups[groupIndex].EmissionRate, Math.Max(rate, 0f));
    }

    /// <summary>Whether a group is visible for simulation and rendering.</summary>
    public bool IsGroupVisible(int groupIndex) => Volatile.Read(ref _groups[groupIndex].Visible);

    /// <summary>Shows or hides a group on this instance without mutating the asset.</summary>
    public void SetGroupVisible(int groupIndex, bool visible)
    {
        ref GroupState group = ref _groups[groupIndex];
        if (Volatile.Read(ref group.Visible) == visible)
            return;
        Volatile.Write(ref group.Visible, visible);
        _system.InvalidatePlan();
    }

    /// <summary>Returns a copy of the group's live <see cref="EmitterParams2D"/> record.</summary>
    public EmitterParams2D GetGroupParams(int groupIndex)
    {
        using Lock.Scope _ = _system.StateLock.EnterScope();
        return _system.ParamsRef(_groups[groupIndex].Slot);
    }

    /// <summary>Replaces the static parameter fields of a group for this instance without respawning.</summary>
    public void SetGroupParams(int groupIndex, in EmitterParams2D parameters)
    {
        using Lock.Scope _ = _system.StateLock.EnterScope();
        ref GroupState group = ref _groups[groupIndex];
        ref EmitterParams2D slot = ref _system.ParamsRef(group.Slot);
        slot = EmitterParams2D.MergeEdited(slot, parameters);
        _system.Pool.UpdateParams(group.Slot, 1);
        group.Lifetime = new ParticleRange(slot.Life.X, slot.Life.Y);
    }

    /// <summary>Resumes emission.</summary>
    public void Play()
    {
        using Lock.Scope _ = _system.StateLock.EnterScope();
        IsPlaying = true;
        for (int i = 0; i < _groups.Length; i++)
        {
            _groups[i].IdleTimer = 0f;
            _groups[i].Active = true;
        }
        IsActive = true;
        _system.InvalidatePlan();
    }

    /// <summary>Stops emission; live particles die out naturally.</summary>
    public void Stop()
    {
        IsPlaying = false;
    }

    /// <summary>Kills every live particle and restarts the emission timeline.</summary>
    public void Restart()
    {
        using Lock.Scope _ = _system.StateLock.EnterScope();
        for (int i = 0; i < _groups.Length; i++)
        {
            ref GroupState group = ref _groups[i];
            _system.Pool.QueueKill(group.Slice);
            group.Cursor = 0;
            Volatile.Write(ref group.Time, 0f);
            group.Accumulator = 0f;
            group.IdleTimer = 0f;
            group.Active = true;
        }
        IsPlaying = true;
        IsActive = true;
        _system.InvalidatePlan();
    }

    internal void AdvanceFrame(float deltaTime, ref uint dirtyMin, ref uint dirtyMax)
    {
        bool anyActive = false;
        bool playing = IsPlaying;
        Transform2D transform = Transform;
        float height = Height;
        float depthBase = DepthBase;

        for (int i = 0; i < _groups.Length; i++)
        {
            ref GroupState group = ref _groups[i];
            ParticleGroup2D asset = group.Asset;
            float time = Volatile.Read(ref group.Time);
            group.SpawnCount = ParticleEmission.AdvanceLifecycle(
                ref time,
                ref group.Accumulator,
                ref group.IdleTimer,
                deltaTime,
                playing,
                Volatile.Read(ref group.EmissionRate),
                asset.Duration,
                asset.Looping,
                asset.Bursts,
                ref _random,
                group.Slice.Capacity,
                group.Lifetime.Max,
                out bool active);
            Volatile.Write(ref group.Time, time);
            group.Active = active;
            if (!group.Active)
                continue;
            anyActive = true;

            ref EmitterParams2D parameters = ref _system.ParamsRef(group.Slot);
            parameters.SpawnCount = group.SpawnCount;
            parameters.EmitCursor = group.Cursor;
            parameters.DeltaTime = deltaTime;
            parameters.EmitterTime = asset.Duration > 0f ? time % asset.Duration : time;
            parameters.FrameSeed = _random.NextUint();
            parameters.WorldMatrix = transform.Matrix;
            parameters.EmitterHeight = height;
            parameters.DepthBase = depthBase;
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
            _system.ReleaseInstance(this);
    }
}
