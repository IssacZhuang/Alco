using System.Numerics;
using System.Threading;
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
/// The shared GPU buffers of one particle dimension (2D or 3D): the particle pool,
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

    private readonly RenderingSystem _rendering;
    private readonly string _name;
    private readonly Lock _gate;
    private readonly TransientBufferPool _stagingPool;

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

    private readonly List<(Alco.Graphics.Buffer Source, Alco.Graphics.Buffer Target, ulong Bytes)> _pendingCopies = [];
    private readonly List<(Alco.Graphics.Buffer Buffer, ulong FrameId)> _retired = [];
    private readonly List<(uint Offset, uint Count)> _pendingKills = [];

    private int _dirtyMin = int.MaxValue;
    private int _dirtyMax = -1;
    private ulong _lastRecordedFrameId;

    /// <summary>Creates the shared particle pool with initial capacities.</summary>
    public unsafe ParticleBufferPool(
        RenderingSystem rendering,
        int particleCapacity = 65536,
        int emitterSlots = 256,
        string name = "particles",
        Lock? gate = null)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(particleCapacity, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(emitterSlots, 0);

        _rendering = rendering;
        _name = name;
        _gate = gate ?? new Lock();
        _stagingPool = new TransientBufferPool(rendering, $"{name}_params_stage");

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

    /// <summary>The per-batch non-indexed indirect draw argument records (<see cref="GpuDrawArgs"/>).</summary>
    public Alco.Graphics.Buffer DrawArgs => _drawArgs;

    /// <summary>The GPU buffer holding the uploaded <typeparamref name="TParams"/> array.</summary>
    public Alco.Graphics.Buffer Params => _params;

    /// <summary>The CPU mirror span for reading/modifying per-slot parameters before upload.</summary>
    public Span<TParams> ParamsSpan => _cpuParams;

    /// <summary>The current particle capacity of the pool.</summary>
    public int ParticleCapacity => _particleCapacity;

    /// <summary>The current emitter-slot count.</summary>
    public int SlotCapacity => _slotCapacity;

    /// <summary>The number of currently allocated emitter slots.</summary>
    public int AllocatedSlotCount
    {
        get
        {
            lock (_gate)
            {
                return _slotCapacity - _freeSlots.Count;
            }
        }
    }

    /// <summary>Whether a buffer migration copy is queued for the next frame recording.</summary>
    public bool HasPendingMigration
    {
        get
        {
            lock (_gate)
            {
                return _pendingCopies.Count > 0;
            }
        }
    }

    /// <summary>Raised under the pool gate whenever any pool buffer grows and swaps.</summary>
    public event Action? Reallocated;

    /// <summary>The slices queued to be zeroed by the next simulation before emit.</summary>
    public IReadOnlyList<(uint Offset, uint Count)> PendingKills
    {
        get
        {
            lock (_gate)
            {
                return _pendingKills;
            }
        }
    }

    /// <summary>Clears pending kills (diagnostics / test helper).</summary>
    public void ClearPendingKills()
    {
        lock (_gate)
        {
            _pendingKills.Clear();
        }
    }

    /// <summary>Atomically drains pending kills into <paramref name="kills"/>.</summary>
    public void DrainPendingKills(List<(uint Offset, uint Count)> kills)
    {
        ArgumentNullException.ThrowIfNull(kills);
        lock (_gate)
        {
            kills.Clear();
            kills.AddRange(_pendingKills);
            _pendingKills.Clear();
        }
    }

    /// <summary>Writes one emitter's parameter record into the CPU mirror and marks it dirty for upload.</summary>
    public void SetParams(uint slot, in TParams value)
    {
        lock (_gate)
        {
            _cpuParams[slot] = value;
            MarkDirtyUnlocked((int)slot, 1);
        }
    }

    /// <summary>Marks a slot range as dirty so it will be staged and copied to GPU on the next upload.</summary>
    public void UpdateParams(uint start, uint count)
    {
        if (count == 0)
            return;
        lock (_gate)
        {
            MarkDirtyUnlocked((int)start, (int)count);
        }
    }

    private void MarkDirtyUnlocked(int start, int count)
    {
        _dirtyMin = Math.Min(_dirtyMin, start);
        _dirtyMax = Math.Max(_dirtyMax, start + count);
    }

    /// <summary>Allocates a power-of-two particle slice of at least <paramref name="capacity"/> slots.</summary>
    public ParticleSlice AllocateSlice(int capacity)
    {
        int rounded = Math.Max(1 << MinSliceShift,
            (int)BitOperations.RoundUpToPowerOf2((uint)Math.Max(capacity, 1)));
        lock (_gate)
        {
            if (_freeSlices.TryGetValue(rounded, out Stack<uint>? free) && free.Count > 0)
                return new ParticleSlice(free.Pop(), (uint)rounded);
            if (_highWater + rounded > (uint)_particleCapacity)
                GrowParticles(_highWater + (uint)rounded);
            ParticleSlice slice = new(_highWater, (uint)rounded);
            _highWater += (uint)rounded;
            return slice;
        }
    }

    /// <summary>Queues a slice zeroing pass before any new emissions.</summary>
    public void QueueKill(in ParticleSlice slice)
    {
        lock (_gate)
        {
            if (slice.Capacity > 0)
                _pendingKills.Add((slice.Offset, slice.Capacity));
        }
    }

    /// <summary>Returns a slice to the free list and queues its GPU zeroing pass.</summary>
    public void FreeSlice(in ParticleSlice slice)
    {
        if (slice.Capacity == 0)
            return;
        lock (_gate)
        {
            if (!_freeSlices.TryGetValue((int)slice.Capacity, out Stack<uint>? free))
            {
                free = new Stack<uint>();
                _freeSlices[(int)slice.Capacity] = free;
            }
            free.Push(slice.Offset);
            QueueKill(slice);
        }
    }

    /// <summary>Allocates an emitter slot (index into <see cref="Params"/>).</summary>
    public uint AllocateSlot()
    {
        lock (_gate)
        {
            if (_freeSlots.Count == 0)
                GrowSlots(_slotCapacity * 2);
            return _freeSlots.Pop();
        }
    }

    /// <summary>Returns an emitter slot to the pool.</summary>
    public void FreeSlot(uint slot)
    {
        lock (_gate)
        {
            _freeSlots.Push(slot);
        }
    }

    /// <summary>
    /// Reclaims completed retired buffers, records pending growth migration copies, and stages
    /// dirty emitter parameters into the GPU buffer via <see cref="TransientBufferPool"/>.
    /// Must be called inside an active <see cref="RenderContext"/> frame outside any scope.
    /// </summary>
    /// <returns>True when at least one <see cref="RenderContext.CopyMemory"/> command was recorded.</returns>
    public unsafe bool RecordUploadsAndMigration(RenderContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        lock (_gate)
        {
            ulong frameId = context.CurrentFrameId;
            _lastRecordedFrameId = frameId;

            for (int i = _retired.Count - 1; i >= 0; i--)
            {
                (Alco.Graphics.Buffer buffer, ulong retiredFrame) = _retired[i];
                if (context.IsFrameCompleted(retiredFrame))
                {
                    buffer.Destroy();
                    _retired.RemoveAt(i);
                }
            }

            bool copied = false;
            for (int i = 0; i < _pendingCopies.Count; i++)
            {
                if (i > 0)
                {
                    // Guard chained growth copies (A -> B -> C) recorded in the same frame.
                    context.Barrier(Stage.Transfer, Access.TransferWrite, Stage.Transfer, Access.TransferRead);
                }
                (Alco.Graphics.Buffer source, Alco.Graphics.Buffer target, ulong bytes) = _pendingCopies[i];
                context.CopyMemory(source.Range.Slice(0, bytes), target.Range.Slice(0, bytes));
                _retired.Add((source, frameId));
                copied = true;
            }
            _pendingCopies.Clear();

            if (_dirtyMax > _dirtyMin)
            {
                int min = Math.Max(0, _dirtyMin);
                int max = Math.Min(_slotCapacity, _dirtyMax);
                int count = max - min;
                if (count > 0)
                {
                    ulong elemSize = (ulong)sizeof(TParams);
                    ulong byteOffset = (ulong)min * elemSize;
                    ulong byteSize = (ulong)count * elemSize;
                    Alco.Graphics.Buffer staging = _stagingPool.Acquire(context, byteSize);
                    staging.SetData(_cpuParams.AsSpan(min, count));
                    context.CopyMemory(staging.Range.Slice(0, byteSize), _params.Range.Slice(byteOffset, byteSize));
                    copied = true;
                }
                _dirtyMin = int.MaxValue;
                _dirtyMax = -1;
            }

            return copied;
        }
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
            _pendingCopies.Add((_particles, newParticles, particleBytes));
        else
            _retired.Add((_particles, _lastRecordedFrameId));

        _retired.Add((_instanceData, _lastRecordedFrameId));
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
        MarkDirtyUnlocked(0, oldCapacity);

        _retired.Add((_drawArgs, _lastRecordedFrameId));
        _retired.Add((_params, _lastRecordedFrameId));
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

        lock (_gate)
        {
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
            _stagingPool.Dispose();
        }
    }
}
