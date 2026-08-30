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
/// the per-emitter render lists, the indirect draw-args records and the CPU-mirrored
/// per-emitter parameter array. Effect instances allocate slices/slots from the pool
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

    private GraphicsBuffer _particles;
    private GraphicsBuffer _renderList;
    private GraphicsBuffer _drawArgs;
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
    public unsafe ParticleBufferPool(RenderingSystem rendering, int particleCapacity = 65536, int emitterSlots = 256, string name = "particles")
    {
        ArgumentNullException.ThrowIfNull(rendering);
        _rendering = rendering;
        _name = name;
        _particleCapacity = particleCapacity;
        _particles = rendering.CreateGraphicsBuffer((uint)(particleCapacity * sizeof(TParticle)), $"{name}_pool");
        _renderList = rendering.CreateGraphicsBuffer((uint)(particleCapacity * sizeof(uint)), $"{name}_render_list");
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

    /// <summary>The shared indirect draw-args buffer (one IndexedIndirectData record per emitter slot).</summary>
    public GraphicsBuffer DrawArgs => _drawArgs;

    /// <summary>The CPU-mirrored per-emitter parameter array.</summary>
    public GraphicsArrayBuffer<TParams> Params => _params;

    /// <summary>The current particle capacity of the pool.</summary>
    public int ParticleCapacity => _particleCapacity;

    /// <summary>The current emitter-slot count.</summary>
    public int SlotCapacity => _slotCapacity;

    /// <summary>The number of currently allocated emitter slots.</summary>
    public int AllocatedSlotCount => _slotCapacity - _freeSlots.Count;

    /// <summary>
    /// Raised after any pool buffer was reallocated (growth): every material bound
    /// to the pool must rebind its buffer slots.
    /// </summary>
    public event Action? Reallocated;

    /// <summary>The slices that must be killed (zeroed) by the next simulation, before any emit.</summary>
    public IReadOnlyList<(uint Offset, uint Count)> PendingKills => _pendingKills;

    /// <summary>Drops the consumed pending-kill list.</summary>
    public void ClearPendingKills() => _pendingKills.Clear();

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

    /// <summary>
    /// Queues a slice's kill dispatch (the GPU zeroing happens at the next
    /// simulation, before any new tenant's first emit) without freeing it — used
    /// by effect restarts that keep their slice.
    /// </summary>
    /// <param name="slice">The slice to zero.</param>
    public void QueueKill(in ParticleSlice slice)
    {
        if (slice.Capacity > 0)
        {
            _pendingKills.Add((slice.Offset, slice.Capacity));
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
        if (!_freeSlices.TryGetValue((int)slice.Capacity, out Stack<uint>? free))
        {
            free = new Stack<uint>();
            _freeSlices[(int)slice.Capacity] = free;
        }
        free.Push(slice.Offset);
        QueueKill(slice);
    }

    /// <summary>Allocates an emitter slot (an index into the params and draw-args arrays).</summary>
    public uint AllocateSlot()
    {
        if (_freeSlots.Count == 0)
        {
            GrowSlots(_slotCapacity * 2);
        }
        return _freeSlots.Pop();
    }

    /// <summary>Returns an emitter slot to the pool.</summary>
    /// <param name="slot">The slot to return.</param>
    public void FreeSlot(uint slot) => _freeSlots.Push(slot);

    /// <summary>
    /// Records the pending growth copies into the frame's command buffer. Must run
    /// before any particle dispatch or draw of the frame, outside any pass.
    /// </summary>
    /// <param name="commandBuffer">The frame's shared command buffer.</param>
    public void RecordMigration(GPUCommandBuffer commandBuffer)
    {
        ArgumentNullException.ThrowIfNull(commandBuffer);
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

    /// <summary>
    /// Whether a growth copy is pending (visible in diagnostics only).
    /// </summary>
    public bool HasPendingMigration => _pendingCopies.Count > 0;

    private unsafe void GrowParticles(uint required)
    {
        int newCapacity = (int)Math.Max((uint)_particleCapacity * 2, System.Numerics.BitOperations.RoundUpToPowerOf2(required));
        GraphicsBuffer newParticles = _rendering.CreateGraphicsBuffer((uint)(newCapacity * sizeof(TParticle)), $"{_name}_pool");
        GraphicsBuffer newRenderList = _rendering.CreateGraphicsBuffer((uint)(newCapacity * sizeof(uint)), $"{_name}_render_list");
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
        _particles = newParticles;
        _renderList = newRenderList;
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
