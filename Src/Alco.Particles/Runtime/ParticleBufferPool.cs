using System.Threading;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.Particles;

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
/// the per-emitter render lists, the draw-args records and per-draw instance data
/// of the material-batched indirect draws, and the CPU-mirrored per-emitter
/// parameter array. Effect instances allocate slices/slots from the pool
/// instead of owning GPU buffers, so creating and destroying effects is pure CPU
/// bookkeeping plus a slice-kill dispatch — no GPU resource churn.
/// <br/>Buffers grow geometrically when exhausted; growth copies the live contents
/// into the new buffers at the next <see cref="RecordMigration"/> (recorded at the
/// start of a frame's simulation, before any consuming dispatch or draw) and raises
/// <see cref="Reallocated"/> so materials rebind. Returned slices are zeroed lazily
/// through the pending-kill queue, dispatched before any new tenant's first emit.
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

    /// <summary>
    /// Serializes every bookkeeping mutation (allocation, free, growth, migration,
    /// kills) and the params writes/uploads. The owning particle system passes its
    /// own gate so pool operations and system operations share one reentrant lock —
    /// growth raises <see cref="Reallocated"/> with the gate held, and the system's
    /// handler re-enters pool/system state under it, so a second lock could deadlock.
    /// </summary>
    private readonly Lock _gate;

    private GraphicsBuffer _particles;
    private GraphicsBuffer _renderList;
    private GraphicsBuffer _drawArgs;
    private GraphicsBuffer _instanceData;
    private GraphicsArrayBuffer<TParams> _params;

    private int _particleCapacity;
    private uint _highWater;
    private readonly Dictionary<int, Stack<uint>> _freeSlices = [];

    private int _slotCapacity;
    private readonly Stack<uint> _freeSlots = new();

    private readonly List<(GraphicsBuffer Source, GraphicsBuffer Target, ulong Bytes)> _pendingCopies = [];
    private readonly List<(GraphicsBuffer Buffer, int FramesLeft)> _retired = [];
    private readonly List<(uint Offset, uint Count)> _pendingKills = [];

    /// <summary>
    /// Creates the pool with its initial capacities.
    /// </summary>
    /// <param name="rendering">The rendering system.</param>
    /// <param name="particleCapacity">The initial particle pool size in particles.</param>
    /// <param name="emitterSlots">The initial emitter-slot count (one slot per emitter group instance).</param>
    /// <param name="name">The diagnostic name prefix of the pool's buffers.</param>
    /// <param name="gate">
    /// The gate to serialize this pool's state with — pass the owning particle
    /// system's gate so pool and system operations share one reentrant lock (growth
    /// raises <see cref="Reallocated"/> with the gate held and the system handler
    /// re-enters system state under it; a second lock could deadlock). Null creates
    /// a private gate.
    /// </param>
    public unsafe ParticleBufferPool(
        RenderingSystem rendering, int particleCapacity = 65536, int emitterSlots = 256, string name = "particles",
        Lock? gate = null)
    {
        ArgumentNullException.ThrowIfNull(rendering);
        _rendering = rendering;
        _name = name;
        _gate = gate ?? new Lock();
        _particleCapacity = particleCapacity;
        _particles = rendering.CreateGraphicsBuffer((uint)(particleCapacity * sizeof(TParticle)), $"{name}_pool");
        _renderList = rendering.CreateGraphicsBuffer((uint)(particleCapacity * sizeof(uint)), $"{name}_render_list");
        // Per-draw instance records: the simulate pass writes them as storage, the
        // render pass fetches them as the instance-step vertex buffer of batched
        // indirect draws (see GpuParticleSystem2D.Render). One record per particle
        // slot bounds the per-frame drawStart addressing.
        _instanceData = rendering.CreateGraphicsBuffer(
            (uint)(particleCapacity * sizeof(uint) * 2), BufferUsage.Storage | BufferUsage.Vertex, $"{name}_instance_data");
        _slotCapacity = emitterSlots;
        _drawArgs = rendering.CreateGraphicsBuffer((uint)(emitterSlots * 20), $"{name}_draw_args");
        _params = rendering.CreateGraphicsArrayBuffer<TParams>(emitterSlots, $"{name}_params");
        for (int i = emitterSlots - 1; i >= 0; i--)
        {
            _freeSlots.Push((uint)i);
        }
    }

    /// <summary>The shared particle pool buffer (RWStructuredBuffer&lt;TParticle&gt;).</summary>
    public GraphicsBuffer Particles => _particles;

    /// <summary>The shared render-list buffer (per-slice compacted particle indices).</summary>
    public GraphicsBuffer RenderList => _renderList;

    /// <summary>
    /// The per-draw instance records (see <see cref="GpuParticleSystem2D"/>): the
    /// simulate pass writes one record per live particle at its draw's
    /// firstInstance-based offset; the render pass binds the buffer as the
    /// instance-step vertex buffer of the batched indirect draws.
    /// </summary>
    public GraphicsBuffer InstanceData => _instanceData;

    /// <summary>The per-frame draw-args buffer of the material-batched indirect
    /// draws: one <see cref="IndexedIndirectData"/> record per visible group,
    /// compacted per material in drawIndex order (the pool sizes it by the emitter
    /// slot count, an upper bound of the per-frame draw count).</summary>
    public GraphicsBuffer DrawArgs => _drawArgs;

    /// <summary>The CPU-mirrored per-emitter parameter array.</summary>
    public GraphicsArrayBuffer<TParams> Params => _params;

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

    /// <summary>
    /// Raised after any pool buffer was reallocated (growth): every material bound
    /// to the pool must rebind its buffer slots. Raised with the pool's gate held;
    /// handlers may re-enter pool and owning-system members (the gate is reentrant).
    /// </summary>
    public event Action? Reallocated;

    /// <summary>The slices that must be killed (zeroed) by the next simulation, before any emit.</summary>
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

    /// <summary>Drops the consumed pending-kill list.</summary>
    public void ClearPendingKills()
    {
        lock (_gate)
        {
            _pendingKills.Clear();
        }
    }

    /// <summary>
    /// Atomically drains the pending kill ranges: returns them as a snapshot and
    /// clears the queue in one step, so a kill queued concurrently with the drain is
    /// never lost between an observe and a clear. The frame simulation calls this
    /// before dispatching the kills.
    /// </summary>
    /// <returns>The kill ranges to dispatch; empty when none are pending.</returns>
    public (uint Offset, uint Count)[] TakePendingKills()
    {
        lock (_gate)
        {
            if (_pendingKills.Count == 0)
            {
                return Array.Empty<(uint Offset, uint Count)>();
            }
            (uint Offset, uint Count)[] kills = _pendingKills.ToArray();
            _pendingKills.Clear();
            return kills;
        }
    }

    /// <summary>
    /// Writes one emitter's parameter record into the CPU mirror (thread-safe;
    /// see <see cref="UpdateParams"/> for the upload).
    /// </summary>
    /// <param name="slot">The emitter slot to write.</param>
    /// <param name="value">The record to store.</param>
    public void SetParams(uint slot, in TParams value)
    {
        lock (_gate)
        {
            _params[(int)slot] = value;
        }
    }

    /// <summary>
    /// Uploads a range of the CPU-mirrored parameter array to the GPU buffer
    /// (thread-safe; serialized against concurrent writes and uploads, including
    /// the frame simulation's dirty-range upload).
    /// </summary>
    /// <param name="start">The first slot of the range.</param>
    /// <param name="count">The number of slots to upload.</param>
    public void UpdateParams(uint start, uint count)
    {
        lock (_gate)
        {
            _params.UpdateBufferRanged(start, count);
        }
    }

    /// <summary>
    /// Allocates a particle slice of at least <paramref name="capacity"/> particles.
    /// Grows the pool when exhausted (see the class remarks). A freshly bump-allocated
    /// slice starts zeroed by the backend; a recycled slice is killed by the pending
    /// kill the freeing side queued.
    /// </summary>
    /// <param name="capacity">The minimum number of particles the slice must hold.</param>
    /// <returns>The allocated slice.</returns>
    public ParticleSlice AllocateSlice(int capacity)
    {
        int rounded = Math.Max(1 << MinSliceShift, (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(capacity, 1)));
        lock (_gate)
        {
            if (_freeSlices.TryGetValue(rounded, out Stack<uint>? free) && free.Count > 0)
            {
                return new ParticleSlice(free.Pop(), (uint)rounded);
            }
            if (_highWater + rounded > (uint)_particleCapacity)
            {
                GrowParticles(_highWater + (uint)rounded);
            }
            ParticleSlice slice = new(_highWater, (uint)rounded);
            _highWater += (uint)rounded;
            return slice;
        }
    }

    /// <summary>
    /// Queues a slice's kill dispatch (the GPU zeroing happens at the next
    /// simulation, before any new tenant's first emit) without freeing it — used
    /// by effect restarts that keep their slice.
    /// </summary>
    /// <param name="slice">The slice to zero.</param>
    public void QueueKill(in ParticleSlice slice)
    {
        lock (_gate)
        {
            if (slice.Capacity > 0)
            {
                _pendingKills.Add((slice.Offset, slice.Capacity));
            }
        }
    }

    /// <summary>
    /// Returns a slice to the pool and queues its kill dispatch (the GPU zeroing
    /// happens at the next simulation, before any new tenant's first emit).
    /// </summary>
    /// <param name="slice">The slice to return.</param>
    public void FreeSlice(in ParticleSlice slice)
    {
        if (slice.Capacity == 0)
        {
            return;
        }
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

    /// <summary>Allocates an emitter slot (an index into the params and draw-args arrays).</summary>
    public uint AllocateSlot()
    {
        lock (_gate)
        {
            if (_freeSlots.Count == 0)
            {
                GrowSlots(_slotCapacity * 2);
            }
            return _freeSlots.Pop();
        }
    }

    /// <summary>Returns an emitter slot to the pool.</summary>
    /// <param name="slot">The slot to return.</param>
    public void FreeSlot(uint slot)
    {
        lock (_gate)
        {
            _freeSlots.Push(slot);
        }
    }

    /// <summary>
    /// Records the pending growth copies into the frame's command buffer. Must run
    /// before any particle dispatch or draw of the frame, outside any pass.
    /// </summary>
    /// <param name="commandBuffer">The frame's shared command buffer.</param>
    public void RecordMigration(GPUCommandBuffer commandBuffer)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
        lock (_gate)
        {
            foreach ((GraphicsBuffer source, GraphicsBuffer target, ulong bytes) in _pendingCopies)
            {
                commandBuffer.CopyBuffer(source.NativeBuffer, target.NativeBuffer, 0, 0, bytes);
                _retired.Add((source, 2));
            }
            _pendingCopies.Clear();
            for (int i = _retired.Count - 1; i >= 0; i--)
            {
                (GraphicsBuffer buffer, int framesLeft) = _retired[i];
                framesLeft--;
                if (framesLeft <= 0)
                {
                    buffer.Dispose();
                    _retired.RemoveAt(i);
                }
                else
                {
                    _retired[i] = (buffer, framesLeft);
                }
            }
        }
    }

    /// <summary>
    /// Whether a growth copy is pending (visible in diagnostics only).
    /// </summary>
    public bool HasPendingMigration => _pendingCopies.Count > 0;

    private unsafe void GrowParticles(uint required)
    {
        int newCapacity = (int)Math.Max((uint)_particleCapacity * 2, System.Numerics.BitOperations.RoundUpToPowerOf2(required));
        GraphicsBuffer newParticles = _rendering.CreateGraphicsBuffer((uint)(newCapacity * sizeof(TParticle)), $"{_name}_pool");
        GraphicsBuffer newRenderList = _rendering.CreateGraphicsBuffer((uint)(newCapacity * sizeof(uint)), $"{_name}_render_list");
        GraphicsBuffer newInstanceData = _rendering.CreateGraphicsBuffer(
            (uint)(newCapacity * sizeof(uint) * 2), BufferUsage.Storage | BufferUsage.Vertex, $"{_name}_instance_data");
        ulong particleBytes = (ulong)_highWater * (ulong)sizeof(TParticle);
        if (particleBytes > 0)
        {
            _pendingCopies.Add((_particles, newParticles, particleBytes));
            _pendingCopies.Add((_renderList, newRenderList, (ulong)_highWater * sizeof(uint)));
        }
        else
        {
            _retired.Add((_particles, 2));
            _retired.Add((_renderList, 2));
        }
        // The instance records are per-frame transient (every drawn group rewrites
        // its whole run each simulate pass), so growth swaps without a copy.
        _retired.Add((_instanceData, 2));
        _particles = newParticles;
        _renderList = newRenderList;
        _instanceData = newInstanceData;
        _particleCapacity = newCapacity;
        Reallocated?.Invoke();
    }

    private void GrowSlots(int newCapacity)
    {
        GraphicsBuffer newDrawArgs = _rendering.CreateGraphicsBuffer((uint)(newCapacity * 20), $"{_name}_draw_args");
        GraphicsArrayBuffer<TParams> newParams = _rendering.CreateGraphicsArrayBuffer<TParams>(newCapacity, $"{_name}_params");
        _params.AsSpan().CopyTo(newParams.AsSpan());
        newParams.UpdateBuffer();
        _pendingCopies.Add((_drawArgs, newDrawArgs, (ulong)_slotCapacity * 20));
        _retired.Add((_params, 2));
        _drawArgs = newDrawArgs;
        _params = newParams;
        for (int i = newCapacity - 1; i >= _slotCapacity; i--)
        {
            _freeSlots.Push((uint)i);
        }
        _slotCapacity = newCapacity;
        Reallocated?.Invoke();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }
        _particles.Dispose();
        _renderList.Dispose();
        _drawArgs.Dispose();
        _instanceData.Dispose();
        _params.Dispose();
        foreach ((GraphicsBuffer buffer, _) in _retired)
        {
            buffer.Dispose();
        }
        _retired.Clear();
        // Sources of growth copies that were never recorded (the pool died before
        // the next RecordMigration) never moved to the retired list — dispose them
        // here. The targets are the live fields above; disposal is idempotent.
        foreach ((GraphicsBuffer source, _, _) in _pendingCopies)
        {
            source.Dispose();
        }
        _pendingCopies.Clear();
    }
}
