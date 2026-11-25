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
/// Shared GPU buffers of one particle dimension (2D or 3D): the particle pool,
/// per-batch indirect draw arguments, compacted instance records, and the CPU-mirrored parameter array.
/// Buffers grow geometrically when exhausted, migrating live contents via <see cref="RecordUploadsAndMigration"/>
/// and retiring outgrown buffers once their last referencing GPU frame has completed.
/// Pool mutations are synchronized by the owning particle system while <see cref="TouchBuffersUsed"/> is safe from lock-free <c>Render()</c> paths.
/// </summary>
/// <typeparam name="TParticle">The particle record type (GPU layout twin).</typeparam>
/// <typeparam name="TParams">The per-emitter parameter record type (GPU layout twin).</typeparam>
internal sealed class ParticleBufferPool<TParticle, TParams> : AutoDisposable
    where TParticle : unmanaged
    where TParams : unmanaged
{
    private const int MinSliceShift = 6; // slices are powers of two, at least 64 particles

    private struct PendingCopy
    {
        public Alco.Graphics.Buffer Source;
        public Alco.Graphics.Buffer Target;
        public ulong Bytes;
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
    private readonly List<Alco.Graphics.Buffer> _staleBuffers = [];
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
    public RenderContext? LastUseContext => Volatile.Read(ref _lastUseContext);

    /// <summary>The frame identifier within <see cref="LastUseContext"/> of the most recent pool buffer access.</summary>
    public ulong LastUseFrameId => Volatile.Read(ref _lastUseFrameId);

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
        RenderContext? lastUseContext = Volatile.Read(ref _lastUseContext);
        ulong lastUseFrameId = Volatile.Read(ref _lastUseFrameId);
        if (_staleBuffers.Count > 0 &&
            (lastUseContext == null || lastUseContext.IsDisposed ||
             !ReferenceEquals(lastUseContext, context) || lastUseContext.CurrentFrameId != lastUseFrameId))
        {
            for (int i = 0; i < _staleBuffers.Count; i++)
                _staleBuffers[i].Dispose();
            _staleBuffers.Clear();
        }

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
                _inFlightCopies[i].Source.Dispose();
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

    /// <summary>Records the most recent context and frame that drew from this pool.</summary>
    public void TouchBuffersUsed(
        RenderContext context,
        Alco.Graphics.Buffer particles,
        Alco.Graphics.Buffer paramsBuffer,
        Alco.Graphics.Buffer drawArgs,
        Alco.Graphics.Buffer instanceData)
    {
        ArgumentNullException.ThrowIfNull(context);
        _ = particles;
        _ = paramsBuffer;
        _ = drawArgs;
        _ = instanceData;
        Volatile.Write(ref _lastUseContext, context);
        Volatile.Write(ref _lastUseFrameId, context.CurrentFrameId);
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
    /// Reconciles pending growth migration copies and stages
    /// dirty emitter parameters into the GPU buffer via <see cref="TransientBufferPool"/>.
    /// Must be called inside an active <see cref="RenderContext"/> frame outside any scope.
    /// </summary>
    /// <returns>True when at least one <see cref="RenderContext.CopyBuffer"/> command was recorded.</returns>
    public unsafe bool RecordUploadsAndMigration(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        ReconcileFrame(context);
        Volatile.Write(ref _lastUseContext, context);
        Volatile.Write(ref _lastUseFrameId, context.CurrentFrameId);

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
            context.CopyBuffer(copy.Source.Slice(0, copy.Bytes), copy.Target.Slice(0, copy.Bytes));
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
                context.CopyBuffer(staging.Slice(0, byteSize), _params.Slice(byteOffset, byteSize));

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
            });
        }
        else
        {
            RetireBuffer(_particles);
        }

        RetireBuffer(_instanceData);
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

        RetireBuffer(_drawArgs);
        RetireBuffer(_params);
        _drawArgs = newDrawArgs;
        _params = newParams;

        for (int i = newCapacity - 1; i >= oldCapacity; i--)
            _freeSlots.Push((uint)i);
        _slotCapacity = newCapacity;
        Reallocated?.Invoke();
    }

    private void RetireBuffer(Alco.Graphics.Buffer buffer)
    {
        RenderContext? lastUseContext = Volatile.Read(ref _lastUseContext);
        ulong lastUseFrameId = Volatile.Read(ref _lastUseFrameId);
        if (lastUseContext != null && !lastUseContext.IsDisposed && lastUseFrameId != 0 &&
            lastUseContext.CurrentFrameId == lastUseFrameId)
        {
            _staleBuffers.Add(buffer);
            return;
        }
        buffer.Dispose();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        _quadIndices.Dispose();
        _particles.Dispose();
        _drawArgs.Dispose();
        _instanceData.Dispose();
        _params.Dispose();
        for (int i = 0; i < _staleBuffers.Count; i++)
            _staleBuffers[i].Dispose();
        _staleBuffers.Clear();
        for (int i = 0; i < _pendingCopies.Count; i++)
            _pendingCopies[i].Source.Dispose();
        _pendingCopies.Clear();
        for (int i = 0; i < _inFlightCopies.Count; i++)
            _inFlightCopies[i].Source.Dispose();
        _inFlightCopies.Clear();
        _inFlightKills.Clear();
        _stagingPool.Dispose();
    }
}
