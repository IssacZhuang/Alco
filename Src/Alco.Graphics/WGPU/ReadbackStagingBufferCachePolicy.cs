using System.Collections.Generic;

namespace Alco.Graphics.WebGPU;

/// <summary>
/// Pure-managed selection and trimming policy for the WebGPU readback staging-buffer
/// cache. Holds no native handles and performs no WebGPU calls, so it can be unit tested
/// in isolation. An instance is owned by <see cref="WebGPUDevice"/> and all access is
/// guarded by the device's staging-cache lock.
/// </summary>
/// <typeparam name="TTicket">An opaque caller-supplied value that identifies a cached buffer (typically a <see cref="WebGPU.WGPUBuffer"/> handle boxed by the caller). The policy treats it as opaque.</typeparam>
internal sealed class ReadbackStagingBufferCachePolicy<TTicket>
{
    private struct IdleEntry
    {
        public TTicket Ticket;
        public ulong Capacity;
        public long LastUsedTimestamp;
    }

    private readonly List<IdleEntry> _idle = new(capacity: 4);
    private readonly ulong _idleBudget;
    private readonly ulong _singleBufferMax;
    private readonly long _idleExpirationTicks;
    private readonly ulong _oversizeReuseThresholdBytes;

    /// <summary>
    /// Running total of cached idle capacity, in bytes.
    /// </summary>
    public ulong IdleCapacityBytes { get; private set; }

    /// <summary>
    /// Number of idle buffers currently cached.
    /// </summary>
    public int IdleCount => _idle.Count;

    /// <param name="idleBudget">Upper bound for total idle cached staging-buffer capacity, in bytes.</param>
    /// <param name="singleBufferMax">Buffers larger than this are allowed for readback but are never cached.</param>
    /// <param name="idleExpirationTicks">Idle buffers older than this (in <see cref="System.Diagnostics.Stopwatch"/> ticks) are eligible for destruction even under budget.</param>
    /// <param name="oversizeReuseThresholdBytes">Floor added to the oversized-reuse check so small readbacks do not consume much larger cached buffers.</param>
    public ReadbackStagingBufferCachePolicy(
        ulong idleBudget,
        ulong singleBufferMax,
        long idleExpirationTicks,
        ulong oversizeReuseThresholdBytes)
    {
        _idleBudget = idleBudget;
        _singleBufferMax = singleBufferMax;
        _idleExpirationTicks = idleExpirationTicks;
        _oversizeReuseThresholdBytes = oversizeReuseThresholdBytes;
    }

    /// <summary>
    /// Rounds a required staging size up to a reuse-friendly bucket so that many readbacks
    /// of nearby sizes share one native buffer. Always returns a value &gt;= <paramref name="required"/>.
    /// </summary>
    public ulong Bucketize(ulong required)
    {
        // Bucket up to the next power of two for small sizes, then to 1 MB steps for large sizes.
        // This maximizes reuse for texture readbacks (fixed sizes) without wasting memory on
        // buffer readbacks (arbitrary sizes).
        const ulong oneMegabyte = 1UL << 20;
        if (required == 0)
        {
            return oneMegabyte;
        }

        if (required <= oneMegabyte)
        {
            return RoundUpToPowerOfTwo(required);
        }

        return ((required + oneMegabyte - 1) / oneMegabyte) * oneMegabyte;
    }

    /// <summary>
    /// Indicates whether a buffer of <paramref name="capacity"/> should be cached when
    /// returned, or destroyed instead. Oversized one-shot buffers are never cached.
    /// </summary>
    public bool ShouldCacheOnReturn(ulong capacity)
    {
        return capacity <= _singleBufferMax;
    }

    /// <summary>
    /// Finds the best-fit idle buffer for a request of <paramref name="required"/> bytes:
    /// the smallest cached capacity that satisfies the request and is not excessively
    /// oversized. On success returns <c>true</c> and sets <paramref name="ticket"/> to the
    /// cached buffer's identifier; the entry is removed from the idle set.
    /// </summary>
    public bool TryAcquire(ulong required, out TTicket ticket)
    {
        // A cached buffer is acceptable if capacity >= required AND it is not excessively
        // oversized: capacity <= max(required * 2, required + threshold).
        ulong oversizeCeiling = required * 2;
        if (oversizeCeiling - required < _oversizeReuseThresholdBytes)
        {
            oversizeCeiling = required + _oversizeReuseThresholdBytes;
        }

        int best = -1;
        ulong bestCapacity = ulong.MaxValue;
        for (int i = 0; i < _idle.Count; i++)
        {
            ulong capacity = _idle[i].Capacity;
            if (capacity < required || capacity > oversizeCeiling)
            {
                continue;
            }

            if (capacity < bestCapacity)
            {
                bestCapacity = capacity;
                best = i;
            }
        }

        if (best < 0)
        {
            ticket = default!;
            return false;
        }

        ticket = _idle[best].Ticket;
        IdleCapacityBytes -= _idle[best].Capacity;
        _idle.RemoveAt(best);
        return true;
    }

    /// <summary>
    /// Returns a buffer to the idle cache, then trims expired and over-budget entries.
    /// <paramref name="evicted"/> receives the tickets of buffers the caller must destroy
    /// natively; it is cleared first and never contains <paramref name="ticket"/>.
    /// </summary>
    public void Return(TTicket ticket, ulong capacity, long nowTimestamp, List<TTicket> evicted)
    {
        evicted.Clear();
        if (capacity > _singleBufferMax)
        {
            // Oversized one-shot: do not cache. Caller keeps the handle and must destroy it.
            return;
        }

        _idle.Add(new IdleEntry { Ticket = ticket, Capacity = capacity, LastUsedTimestamp = nowTimestamp });
        IdleCapacityBytes += capacity;
        Trim(nowTimestamp, evicted);
    }

    /// <summary>
    /// Drops idle buffers that have expired by age (even under budget) or that push the
    /// cache over budget (oldest first). Populates <paramref name="evicted"/> with the
    /// tickets of removed entries.
    /// </summary>
    public void Trim(long nowTimestamp, List<TTicket> evicted)
    {
        evicted.Clear();

        // First pass: drop expired entries.
        int write = 0;
        for (int read = 0; read < _idle.Count; read++)
        {
            if (nowTimestamp - _idle[read].LastUsedTimestamp >= _idleExpirationTicks)
            {
                evicted.Add(_idle[read].Ticket);
                IdleCapacityBytes -= _idle[read].Capacity;
            }
            else
            {
                _idle[write++] = _idle[read];
            }
        }

        _idle.RemoveRange(write, _idle.Count - write);

        // Second pass: if still over budget, evict oldest (head) first. The list is kept in
        // insertion order which is also LRU order, since TryAcquire removes rather than
        // reorders and Return always appends at the tail.
        while (IdleCapacityBytes > _idleBudget && _idle.Count > 0)
        {
            evicted.Add(_idle[0].Ticket);
            IdleCapacityBytes -= _idle[0].Capacity;
            _idle.RemoveAt(0);
        }
    }

    /// <summary>
    /// Clears all idle entries and returns the tickets of the buffers the caller must destroy.
    /// </summary>
    public void Drain(List<TTicket> evicted)
    {
        evicted.Clear();
        foreach (IdleEntry entry in _idle)
        {
            evicted.Add(entry.Ticket);
        }

        _idle.Clear();
        IdleCapacityBytes = 0;
    }

    private static ulong RoundUpToPowerOfTwo(ulong value)
    {
        if (value <= 1)
        {
            return 1;
        }

        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        value |= value >> 32;
        return value + 1;
    }
}
