using System.Numerics;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Effects;

/// <summary>A particle pool slice: a contiguous run of particle slots inside the shared pool buffer.</summary>
internal readonly struct ParticleSlice
{
    /// <summary>The absolute first slot of the slice in the pool buffer.</summary>
    public readonly uint Offset;

    /// <summary>The capacity of the slice in particles (power of two, at least 64).</summary>
    public readonly uint Capacity;

    public ParticleSlice(uint offset, uint capacity)
    {
        Offset = offset;
        Capacity = capacity;
    }
}

/// <summary>
/// Main-thread-only shared GPU buffers of one particle dimension (2D or 3D): the particle pool,
/// per-batch indirect draw arguments, compacted instance records, and the CPU-mirrored parameter array.
/// Buffers grow geometrically when exhausted, migrating live contents via <see cref="RecordUploadsAndMigration"/>
/// and retiring outgrown buffers once their last referencing GPU frame has completed.
/// </summary>
/// <typeparam name="TParticle">The particle record type (GPU layout twin).</typeparam>
/// <typeparam name="TParams">The per-emitter parameter record type (GPU layout twin).</typeparam>
internal sealed class ParticleBufferPool<TParticle, TParams> : AutoDisposable
    where TParticle : unmanaged
    where TParams : unmanaged
{
    private const int MinSliceShift = 6; // slices are powers of two, at least 64 particles

    private struct RetiredBuffer
    {
        public Alco.Graphics.Buffer Buffer;
        public RenderContext? Context;
        public ulong FrameId;
        public RenderContext? ExtraContext;
        public ulong ExtraFrameId;

        public readonly bool IsCompleted =>
            (Context == null || Context.IsFrameCompleted(FrameId)) &&
            (ExtraContext == null || ExtraContext.IsFrameCompleted(ExtraFrameId));
    }

    private struct PendingCopy
    {
        public Alco.Graphics.Buffer Source;
        public Alco.Graphics.Buffer Target;
        public ulong Bytes;
        public RenderContext? SourceUseContext;
        public ulong SourceUseFrameId;
    }

    private readonly RenderingSystem _rendering;
    private readonly TransientBufferPool _stagingPool;
    private readonly Alco.Graphics.Buffer _quadIndices;

    private Alco.Graphics.Buffer _particles;
    private Alco.Graphics.Buffer _drawArgs;
    private Alco.Graphics.Buffer _instanceData;
    private Alco.Graphics.Buffer _params;
    private TParams[] _cpuParams;

    private int _particleCapacity;
    private uint _highWater;
    private readonly Dictionary<int, Stack<uint>> _freeSlices = [];

    private int _slotCapacity;
    private readonly Stack<uint> _freeSlots = new();

    private readonly List<PendingCopy> _pendingCopies = [];
    private readonly List<PendingCopy> _inFlightCopies = [];
    private readonly List<RetiredBuffer> _retired = [];
    private readonly List<(uint Offset, uint Count)> _pendingKills = [];
    private readonly List<(uint Offset, uint Count)> _inFlightKills = [];

    private int _dirtyMin = int.MaxValue;
    private int _dirtyMax = -1;
    private int _inFlightDirtyMin = int.MaxValue;
    private int _inFlightDirtyMax = -1;
    private RenderContext? _lastUseContext;
    private ulong _lastUseFrameId;
    private RenderContext? _unconfirmedContext;
    private ulong _unconfirmedFrameId;
    private ulong _unconfirmedDiscardVersion;
    private bool _hasUnconfirmedFrame;

    /// <summary>Creates the shared particle pool with initial capacities.</summary>
    public unsafe ParticleBufferPool(
        RenderingSystem rendering,
        int particleCapacity = 65536,
        int emitterSlots = 256)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(particleCapacity, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(emitterSlots, 0);

        _rendering = rendering;
        _stagingPool = new TransientBufferPool(rendering);
        _quadIndices = rendering.CreateBuffer<ushort>([0, 1, 2, 0, 2, 3]);

        _particleCapacity = particleCapacity;
        _particles = rendering.CreateBuffer((ulong)particleCapacity * (ulong)sizeof(TParticle));
        _instanceData = rendering.CreateBuffer((ulong)particleCapacity * (ulong)sizeof(GpuInstanceData));

        _slotCapacity = emitterSlots;
        _drawArgs = rendering.CreateBuffer((ulong)emitterSlots * (ulong)sizeof(GpuDrawArgs));
        _params = rendering.CreateBuffer((ulong)emitterSlots * (ulong)sizeof(TParams));
        _cpuParams = new TParams[emitterSlots];

        for (int i = emitterSlots - 1; i >= 0; i--)
            _freeSlots.Push((uint)i);
    }

    /// <summary>The shared particle pool buffer (<c>ParticleData2D/3D</c>).</summary>
    public Alco.Graphics.Buffer Particles => _particles;

    /// <summary>The per-frame instance records (<see cref="GpuInstanceData"/>).</summary>
    public Alco.Graphics.Buffer InstanceData => _instanceData;

    /// <summary>The per-batch indexed indirect draw argument records (<see cref="GpuDrawArgs"/>).</summary>
    public Alco.Graphics.Buffer DrawArgs => _drawArgs;

    /// <summary>The shared static 6-index quad buffer (<c>[0, 1, 2, 0, 2, 3]</c>) for indexed indirect draws.</summary>
    public Alco.Graphics.Buffer QuadIndices => _quadIndices;

    /// <summary>The GPU buffer holding the uploaded <typeparamref name="TParams"/> array.</summary>
    public Alco.Graphics.Buffer Params => _params;

    /// <summary>The CPU mirror span for reading/modifying per-slot parameters before upload.</summary>
    public Span<TParams> ParamsSpan => _cpuParams;

    /// <summary>The current particle capacity of the pool.</summary>
    public int ParticleCapacity => _particleCapacity;

    /// <summary>The current emitter-slot count.</summary>
    public int SlotCapacity => _slotCapacity;

    /// <summary>The number of currently allocated emitter slots.</summary>
    public int AllocatedSlotCount => _slotCapacity - _freeSlots.Count;

    /// <summary>The most recent render context that accessed the pool buffers.</summary>
    public RenderContext? LastUseContext => _lastUseContext;

    /// <summary>The frame identifier within <see cref="LastUseContext"/> of the most recent pool buffer access.</summary>
    public ulong LastUseFrameId => _lastUseFrameId;

    /// <summary>Whether any particle slice initialization/kill work is queued.</summary>
    public bool HasPendingKills => _pendingKills.Count > 0;

    /// <summary>Whether a buffer migration copy is queued for the next frame recording.</summary>
    public bool HasPendingMigration => _pendingCopies.Count > 0;

    /// <summary>Raised whenever any pool buffer grows and swaps.</summary>
    public event Action? Reallocated;

    /// <summary>The slices queued to be zeroed by the next simulation before emit.</summary>
    public IReadOnlyList<(uint Offset, uint Count)> PendingKills => _pendingKills;

    /// <summary>Clears pending kills (diagnostics / test helper).</summary>
    public void ClearPendingKills() => _pendingKills.Clear();

    /// <summary>Reconciles the previous unconfirmed frame and drains pending kills into <paramref name="kills"/>.</summary>
    public void DrainPendingKills(RenderContext context, List<(uint Offset, uint Count)> kills)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(kills);
        ReconcileFrame(context);
        kills.Clear();
        kills.AddRange(_pendingKills);
        if (_pendingKills.Count > 0)
        {
            _inFlightKills.AddRange(_pendingKills);
            _pendingKills.Clear();
            MarkFrameUnconfirmed(context);
        }
    }

    /// <summary>Reconciles an unconfirmed prior frame (restoring uploads, migrations, and kills if discarded).</summary>
    /// <returns>True when the prior frame was discarded and pending work was restored.</returns>
    public bool ReconcileFrame(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_hasUnconfirmedFrame)
            return false;

        RenderContext? unconfirmed = _unconfirmedContext;
        if (unconfirmed != null && unconfirmed.DiscardVersion != _unconfirmedDiscardVersion)
        {
            if (_inFlightCopies.Count > 0)
            {
                _pendingCopies.InsertRange(0, _inFlightCopies);
                _inFlightCopies.Clear();
            }
            if (_inFlightDirtyMax > _inFlightDirtyMin)
            {
                MarkDirty(_inFlightDirtyMin, _inFlightDirtyMax - _inFlightDirtyMin);
                _inFlightDirtyMin = int.MaxValue;
                _inFlightDirtyMax = -1;
            }
            if (_inFlightKills.Count > 0)
            {
                _pendingKills.InsertRange(0, _inFlightKills);
                _inFlightKills.Clear();
            }
            _unconfirmedContext = null;
            _hasUnconfirmedFrame = false;
            return true;
        }

        if (!ReferenceEquals(unconfirmed, context) || _unconfirmedFrameId != context.CurrentFrameId)
        {
            for (int i = 0; i < _inFlightCopies.Count; i++)
            {
                PendingCopy copy = _inFlightCopies[i];
                _retired.Add(new RetiredBuffer
                {
                    Buffer = copy.Source,
                    Context = unconfirmed,
                    FrameId = _unconfirmedFrameId,
                    ExtraContext = copy.SourceUseContext,
                    ExtraFrameId = copy.SourceUseFrameId,
                });
            }
            _inFlightCopies.Clear();
            _inFlightKills.Clear();
            _inFlightDirtyMin = int.MaxValue;
            _inFlightDirtyMax = -1;
            _unconfirmedContext = null;
            _hasUnconfirmedFrame = false;
        }

        return false;
    }

    private void MarkFrameUnconfirmed(RenderContext context)
    {
        _unconfirmedContext = context;
        _unconfirmedFrameId = context.CurrentFrameId;
        _unconfirmedDiscardVersion = context.DiscardVersion;
        _hasUnconfirmedFrame = true;
    }

    /// <summary>Records that <paramref name="context"/> drew from the given buffer snapshot, pinning retired entries until completion.</summary>
    public void TouchBuffersUsed(
        RenderContext context,
        Alco.Graphics.Buffer particles,
        Alco.Graphics.Buffer paramsBuffer,
        Alco.Graphics.Buffer drawArgs,
        Alco.Graphics.Buffer instanceData)
    {
        ArgumentNullException.ThrowIfNull(context);
        ulong frameId = context.CurrentFrameId;
        _lastUseContext = context;
        _lastUseFrameId = frameId;

        if (ReferenceEquals(particles, _particles)
            && ReferenceEquals(paramsBuffer, _params)
            && ReferenceEquals(drawArgs, _drawArgs)
            && ReferenceEquals(instanceData, _instanceData))
        {
            return;
        }

        for (int i = 0; i < _retired.Count; i++)
        {
            RetiredBuffer entry = _retired[i];
            if (!IsAnyBuffer(entry.Buffer, particles, paramsBuffer, drawArgs, instanceData))
                continue;
            PinRetiredUse(ref entry, context, frameId);
            _retired[i] = entry;
        }

        for (int i = 0; i < _pendingCopies.Count; i++)
        {
            PendingCopy copy = _pendingCopies[i];
            if (!IsAnyBuffer(copy.Source, particles, paramsBuffer, drawArgs, instanceData))
                continue;
            copy.SourceUseContext = context;
            copy.SourceUseFrameId = frameId;
            _pendingCopies[i] = copy;
        }

        for (int i = 0; i < _inFlightCopies.Count; i++)
        {
            PendingCopy copy = _inFlightCopies[i];
            if (!IsAnyBuffer(copy.Source, particles, paramsBuffer, drawArgs, instanceData))
                continue;
            copy.SourceUseContext = context;
            copy.SourceUseFrameId = frameId;
            _inFlightCopies[i] = copy;
        }
    }

    private static bool IsAnyBuffer(
        Alco.Graphics.Buffer candidate,
        Alco.Graphics.Buffer particles,
        Alco.Graphics.Buffer paramsBuffer,
        Alco.Graphics.Buffer drawArgs,
        Alco.Graphics.Buffer instanceData)
        => ReferenceEquals(candidate, particles)
            || ReferenceEquals(candidate, paramsBuffer)
            || ReferenceEquals(candidate, drawArgs)
            || ReferenceEquals(candidate, instanceData);

    private static void PinRetiredUse(ref RetiredBuffer entry, RenderContext context, ulong frameId)
    {
        if (entry.Context == null || ReferenceEquals(entry.Context, context) || entry.Context.IsFrameCompleted(entry.FrameId))
        {
            entry.Context = context;
            entry.FrameId = frameId;
        }
        else
        {
            entry.ExtraContext = context;
            entry.ExtraFrameId = frameId;
        }
    }

    /// <summary>Writes one emitter's parameter record into the CPU mirror and marks it dirty for upload.</summary>
    public void SetParams(uint slot, in TParams value)
    {
        _cpuParams[slot] = value;
        MarkDirty((int)slot, 1);
    }

    /// <summary>Marks a slot range as dirty so it will be staged and copied to GPU on the next upload.</summary>
    public void UpdateParams(uint start, uint count)
    {
        if (count == 0)
            return;
        MarkDirty((int)start, (int)count);
    }

    private void MarkDirty(int start, int count)
    {
        _dirtyMin = Math.Min(_dirtyMin, start);
        _dirtyMax = Math.Max(_dirtyMax, start + count);
    }

    /// <summary>Allocates a power-of-two particle slice of at least <paramref name="capacity"/> slots.</summary>
    public ParticleSlice AllocateSlice(int capacity)
    {
        int rounded = Math.Max(1 << MinSliceShift,
            (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(capacity, 1)));
        if (_freeSlices.TryGetValue(rounded, out Stack<uint>? free) && free.Count > 0)
            return new ParticleSlice(free.Pop(), (uint)rounded);
        if (_highWater + rounded > (uint)_particleCapacity)
            GrowParticles(_highWater + (uint)rounded);
        ParticleSlice slice = new(_highWater, (uint)rounded);
        _highWater += (uint)rounded;
        QueueKill(in slice);
        return slice;
    }

    /// <summary>Queues a slice zeroing pass before any new emissions.</summary>
    public void QueueKill(in ParticleSlice slice)
    {
        if (slice.Capacity > 0)
            _pendingKills.Add((slice.Offset, slice.Capacity));
    }

    /// <summary>Returns a slice to the free list and queues its GPU zeroing pass.</summary>
    public void FreeSlice(in ParticleSlice slice)
    {
        if (slice.Capacity == 0)
            return;
        if (!_freeSlices.TryGetValue((int)slice.Capacity, out Stack<uint>? free))
        {
            free = new Stack<uint>();
            _freeSlices[(int)slice.Capacity] = free;
        }
        free.Push(slice.Offset);
        QueueKill(in slice);
    }

    /// <summary>Allocates an emitter slot (index into <see cref="Params"/>).</summary>
    public uint AllocateSlot()
    {
        if (_freeSlots.Count == 0)
            GrowSlots(_slotCapacity * 2);
        return _freeSlots.Pop();
    }

    /// <summary>Returns an emitter slot to the pool.</summary>
    public void FreeSlot(uint slot) => _freeSlots.Push(slot);

    /// <summary>
    /// Reclaims completed retired buffers, records pending growth migration copies, and stages
    /// dirty emitter parameters into the GPU buffer via <see cref="TransientBufferPool"/>.
    /// Must be called inside an active <see cref="RenderContext"/> frame outside any scope.
    /// </summary>
    /// <returns>True when at least one <see cref="RenderContext.CopyMemory"/> command was recorded.</returns>
    public unsafe bool RecordUploadsAndMigration(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ReconcileFrame(context);
        _lastUseContext = context;
        _lastUseFrameId = context.CurrentFrameId;

        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            if (_retired[i].IsCompleted)
            {
                _retired[i].Buffer.Destroy();
                int last = _retired.Count - 1;
                if (i != last)
                    _retired[i] = _retired[last];
                _retired.RemoveAt(last);
            }
        }

        int dirtyMin = 0;
        int dirtyCount = 0;
        if (_dirtyMax > _dirtyMin)
        {
            dirtyMin = Math.Max(0, _dirtyMin);
            int dirtyMax = Math.Min(_slotCapacity, _dirtyMax);
            dirtyCount = Math.Max(0, dirtyMax - dirtyMin);
        }

        bool hasTransfers = _pendingCopies.Count > 0 || dirtyCount > 0;
        if (hasTransfers)
        {
            context.Barrier(
                Stage.Compute | Stage.Vertex | Stage.Indirect,
                Access.ShaderRead | Access.ShaderWrite | Access.IndirectRead,
                Stage.Transfer,
                Access.TransferRead | Access.TransferWrite);
        }

        for (int i = 0; i < _pendingCopies.Count; i++)
        {
            if (i > 0)
            {
                // Guard chained growth copies (A -> B -> C) recorded in the same frame.
                context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
            }
            PendingCopy copy = _pendingCopies[i];
            context.CopyMemory(copy.Source.Range.Slice(0, copy.Bytes), copy.Target.Range.Slice(0, copy.Bytes));
            _inFlightCopies.Add(copy);
        }
        _pendingCopies.Clear();

        if (_dirtyMax > _dirtyMin)
        {
            if (dirtyCount > 0)
            {
                ulong elemSize = (ulong)sizeof(TParams);
                ulong byteOffset = (ulong)dirtyMin * elemSize;
                ulong byteSize = (ulong)dirtyCount * elemSize;
                Alco.Graphics.Buffer staging = _stagingPool.Acquire(context, byteSize);
                staging.SetData(_cpuParams.AsSpan(dirtyMin, dirtyCount));
                context.CopyMemory(staging.Range.Slice(0, byteSize), _params.Range.Slice(byteOffset, byteSize));

                _inFlightDirtyMin = Math.Min(_inFlightDirtyMin, dirtyMin);
                _inFlightDirtyMax = Math.Max(_inFlightDirtyMax, dirtyMin + dirtyCount);
            }
            _dirtyMin = int.MaxValue;
            _dirtyMax = -1;
        }

        if (hasTransfers)
            MarkFrameUnconfirmed(context);

        return hasTransfers;
    }

    private unsafe void GrowParticles(uint required)
    {
        int newCapacity = (int)Math.Max((uint)_particleCapacity * 2, BitOperations.RoundUpToPowerOf2(required));
        Alco.Graphics.Buffer newParticles = _rendering.CreateBuffer(
            (ulong)newCapacity * (ulong)sizeof(TParticle));
        Alco.Graphics.Buffer newInstanceData = _rendering.CreateBuffer(
            (ulong)newCapacity * (ulong)sizeof(GpuInstanceData));

        ulong particleBytes = (ulong)_highWater * (ulong)sizeof(TParticle);
        if (particleBytes > 0)
        {
            _pendingCopies.Add(new PendingCopy
            {
                Source = _particles,
                Target = newParticles,
                Bytes = particleBytes,
                SourceUseContext = _lastUseContext,
                SourceUseFrameId = _lastUseFrameId,
            });
        }
        else
        {
            _retired.Add(new RetiredBuffer { Buffer = _particles, Context = _lastUseContext, FrameId = _lastUseFrameId });
        }

        _retired.Add(new RetiredBuffer { Buffer = _instanceData, Context = _lastUseContext, FrameId = _lastUseFrameId });
        _particles = newParticles;
        _instanceData = newInstanceData;
        _particleCapacity = newCapacity;
        Reallocated?.Invoke();
    }

    private unsafe void GrowSlots(int newCapacity)
    {
        Alco.Graphics.Buffer newDrawArgs = _rendering.CreateBuffer(
            (ulong)newCapacity * (ulong)sizeof(GpuDrawArgs));
        Alco.Graphics.Buffer newParams = _rendering.CreateBuffer(
            (ulong)newCapacity * (ulong)sizeof(TParams));

        int oldCapacity = _slotCapacity;
        Array.Resize(ref _cpuParams, newCapacity);
        MarkDirty(0, oldCapacity);

        _retired.Add(new RetiredBuffer { Buffer = _drawArgs, Context = _lastUseContext, FrameId = _lastUseFrameId });
        _retired.Add(new RetiredBuffer { Buffer = _params, Context = _lastUseContext, FrameId = _lastUseFrameId });
        _drawArgs = newDrawArgs;
        _params = newParams;

        for (int i = newCapacity - 1; i >= oldCapacity; i--)
            _freeSlots.Push((uint)i);
        _slotCapacity = newCapacity;
        Reallocated?.Invoke();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        _quadIndices.Destroy();
        _particles.Destroy();
        _drawArgs.Destroy();
        _instanceData.Destroy();
        _params.Destroy();
        for (int i = 0; i < _retired.Count; i++)
            _retired[i].Buffer.Destroy();
        _retired.Clear();
        for (int i = 0; i < _pendingCopies.Count; i++)
            _pendingCopies[i].Source.Destroy();
        _pendingCopies.Clear();
        for (int i = 0; i < _inFlightCopies.Count; i++)
            _inFlightCopies[i].Source.Destroy();
        _inFlightCopies.Clear();
        _inFlightKills.Clear();
        _stagingPool.Dispose();
    }
}
