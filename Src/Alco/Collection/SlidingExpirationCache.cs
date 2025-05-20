using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Alco;

/// <summary>
/// Represents a thread-safe cache that evicts entries after a period of inactivity.
/// </summary>
/// <typeparam name="TKey">The type of keys used to identify cache entries.</typeparam>
public sealed class SlidingExpirationCache<TKey> where TKey : notnull
{
    private int _evictLock;
    private long _lastEvictedTicks; // timestamp of latest eviction operation.
    private readonly long _evictionIntervalTicks; // min timespan needed to trigger a new evict operation.
    private readonly long _slidingExpirationTicks; // max timespan allowed for cache entries to remain inactive.
    private readonly ConcurrentDictionary<TKey, CacheEntry> _cache = new();

    /// <summary>
    /// Initializes a new instance of the SlidingExpirationCache class.
    /// </summary>
    /// <param name="slidingExpiration">The sliding expiration time after which inactive entries will be evicted.</param>
    /// <param name="evictionInterval">The interval at which the cache checks for expired entries.</param>
    public SlidingExpirationCache(TimeSpan slidingExpiration, TimeSpan evictionInterval)
    {
        _slidingExpirationTicks = slidingExpiration.Ticks;
        _evictionIntervalTicks = evictionInterval.Ticks;
        _lastEvictedTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// Gets an existing cache entry or adds a new one if it doesn't exist.
    /// </summary>
    /// <typeparam name="TValue">The type of the cached value.</typeparam>
    /// <param name="key">The cache key to access.</param>
    /// <param name="valueFactory">Factory method to create a new value if missing.</param>
    /// <returns>The cached value associated with the specified key.</returns>
    public TValue GetOrAdd<TValue>(TKey key, Func<TKey, TValue> valueFactory) where TValue : class?
    {
        CacheEntry entry = _cache.GetOrAdd(
            key,

            static (TKey key, Func<TKey, TValue> valueFactory) => new(valueFactory(key)),
            valueFactory);

        long utcNowTicks = DateTime.UtcNow.Ticks;
        Volatile.Write(ref entry.LastUsedTicks, utcNowTicks);

        if (utcNowTicks - Volatile.Read(ref _lastEvictedTicks) >= _evictionIntervalTicks)
        {
            if (Interlocked.CompareExchange(ref _evictLock, 1, 0) == 0)
            {
                if (utcNowTicks - _lastEvictedTicks >= _evictionIntervalTicks)
                {
                    EvictStaleCacheEntries(utcNowTicks);
                    Volatile.Write(ref _lastEvictedTicks, utcNowTicks);
                }

                Volatile.Write(ref _evictLock, 0);
            }
        }

        return (TValue)entry.Value!;
    }

    /// <summary>
    /// Removes all entries from the cache and resets the eviction timer.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _lastEvictedTicks = DateTime.UtcNow.Ticks;
    }

    private void EvictStaleCacheEntries(long utcNowTicks)
    {
        foreach (KeyValuePair<TKey, CacheEntry> kvp in _cache)
        {
            if (utcNowTicks - Volatile.Read(ref kvp.Value.LastUsedTicks) >= _slidingExpirationTicks)
            {
                _cache.TryRemove(kvp.Key, out _);
            }
        }
    }

    private sealed class CacheEntry
    {
        public readonly object? Value;
        public long LastUsedTicks;

        public CacheEntry(object? value)
        {
            Value = value;
        }
    }
}

