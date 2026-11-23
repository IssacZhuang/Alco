using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco.Rendering;

/// <summary>
/// Thread-safe dirty-slot publish target notified when an <see cref="InstanceSlot{TInstance}"/>
/// changes its mirrored CPU data.
/// </summary>
public interface IInstanceSlotSink
{
    /// <summary>Marks the pool slot index whose data changed for GPU upload.</summary>
    /// <param name="slot">Global slot index inside the owning pool.</param>
    void MarkSlotDirty(int slot);
}

/// <summary>
/// Per-object write handle into a <see cref="GpuInstancePool{TInstance}"/> mirror chunk.
/// Writes deduplicate unchanged payloads and notify the pool for incremental GPU scatter.
/// </summary>
/// <typeparam name="TInstance">Unmanaged GPU instance struct type.</typeparam>
public sealed class InstanceSlot<TInstance> where TInstance : unmanaged
{
    private TInstance[]? _chunk;
    private int _chunkOffset;
    private IInstanceSlotSink? _sink;
    private int _poolSlot = -1;
    private TInstance _last;
    private bool _hasLast;

    /// <summary>Gets whether this slot is currently bound to a live pool entry.</summary>
    public bool IsAttached => Volatile.Read(ref _chunk) != null;

    /// <summary>Gets the global slot index inside the owning pool, or -1 when detached.</summary>
    public int PoolSlotIndex => _poolSlot;

    /// <summary>Binds this slot to a mirror chunk location inside a pool. Render thread only.</summary>
    /// <param name="chunk">The pool mirror chunk array.</param>
    /// <param name="chunkOffset">Element index within <paramref name="chunk"/>.</param>
    /// <param name="sink">Optional dirty-notification sink.</param>
    /// <param name="poolSlot">Global pool slot index.</param>
    public void Attach(TInstance[] chunk, int chunkOffset, IInstanceSlotSink? sink, int poolSlot)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        _chunkOffset = chunkOffset;
        _sink = sink;
        _poolSlot = poolSlot;
        _last = chunk[chunkOffset];
        _hasLast = true;
        Volatile.Write(ref _chunk, chunk);
    }

    /// <summary>Clears the binding without mutating the recycled slot mirror. Render thread only.</summary>
    public void Clear()
    {
        Volatile.Write(ref _chunk, null);
        _sink = null;
        _poolSlot = -1;
        _hasLast = false;
    }

    /// <summary>
    /// Zeroes the mirrored slot immediately and notifies the pool so GPU rendering stops
    /// drawing this instance even before deferred detachment flushes. Thread-safe.
    /// </summary>
    public void Detach()
    {
        TInstance[]? chunk = Interlocked.Exchange(ref _chunk, null);
        if (chunk != null)
        {
            chunk[_chunkOffset] = default;
            _sink?.MarkSlotDirty(_poolSlot);
        }

        _sink = null;
        _poolSlot = -1;
        _hasLast = false;
    }

    /// <summary>
    /// Writes updated instance data to the mirrored slot if any byte differs from the last
    /// published payload, marking the slot dirty for the next GPU sync.
    /// </summary>
    /// <param name="data">The new instance payload.</param>
    /// <returns><see langword="true"/> when the payload changed and was published.</returns>
    public bool Write(in TInstance data)
    {
        TInstance[]? chunk = Volatile.Read(ref _chunk);
        if (chunk == null)
        {
            return false;
        }

        if (_hasLast)
        {
            ReadOnlySpan<byte> prevBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref _last, 1));
            ReadOnlySpan<byte> nextBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in data), 1));
            if (prevBytes.SequenceEqual(nextBytes))
            {
                return false;
            }
        }

        _last = data;
        _hasLast = true;
        chunk[_chunkOffset] = data;
        _sink?.MarkSlotDirty(_poolSlot);
        return true;
    }
}
