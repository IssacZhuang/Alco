using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

/// <summary>
/// Implements a thread-safe Least Recently Used (LRU) cache with weighted capacity management.
/// </summary>
/// <typeparam name="TKey">The type of keys in the cache. Must be non-null.</typeparam>
/// <typeparam name="TValue">The type of values in the cache. Must be a reference type.</typeparam>
/// <remarks>
/// This cache automatically removes the least recently used items when it reaches capacity.
/// Each item can have a custom weight, allowing for more flexible capacity management.
/// All operations are thread-safe using ConcurrentDictionary and lock mechanism.
/// </remarks>
public class ConcurrentLruCache<TKey, TValue> where TKey : notnull where TValue : class
{
    private struct CacheItem
    {
        public TKey Key;
        public TValue Value;
        public int Weight;

        public CacheItem(TKey key, TValue value, int weight)
        {
            Key = key;
            Value = value;
            Weight = weight;
        }
    }

    private readonly ConcurrentDictionary<TKey, LinkedListNode<CacheItem>> _cacheMap;
    private readonly LinkedList<CacheItem> _lruList;
    private readonly int _capacity;
    private int _sumWeight = 0;
    private readonly object _lock = new object();

    /// <summary>
    /// Gets the number of items currently in the cache.
    /// </summary>
    public int Count => _cacheMap.Count;

    /// <summary>
    /// Gets the maximum capacity of the cache, measured in weight units.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Gets the current total weight of all items in the cache.
    /// </summary>
    public int SumWeight
    {
        get
        {
            lock (_lock)
            {
                return _sumWeight;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentLruCache{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="capacity">The maximum capacity of the cache, measured in weight units.</param>
    public ConcurrentLruCache(int capacity)
    {
        _capacity = capacity > 0 ? capacity : throw new ArgumentException("Capacity must be greater than zero.", nameof(capacity));
        _cacheMap = new ConcurrentDictionary<TKey, LinkedListNode<CacheItem>>();
        _lruList = new LinkedList<CacheItem>();
    }

    /// <summary>
    /// Gets or sets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key of the value to get or set.</param>
    /// <returns>The value associated with the specified key.</returns>
    /// <exception cref="KeyNotFoundException">Thrown when the key is not found in the cache during a get operation.</exception>
    /// <remarks>
    /// When getting a value, the item is marked as recently used.
    /// When setting a value, if the key already exists, the old value is replaced and marked as recently used.
    /// </remarks>
    public TValue this[TKey key]
    {
        get
        {
            lock (_lock)
            {
                if (_cacheMap.TryGetValue(key, out var node))
                {
                    // Move to front (most recently used)
                    _lruList.Remove(node);
                    _lruList.AddFirst(node);
                    return node.Value.Value;
                }
            }
            throw new KeyNotFoundException($"The key '{key}' was not found in the cache.");
        }
        set
        {
            Set(key, value);
        }
    }

    /// <summary>
    /// Attempts to get the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key of the value to get.</param>
    /// <param name="result">When this method returns, contains the value associated with the specified key, if the key is found; otherwise, the default value for the type of the value parameter.</param>
    /// <returns><c>true</c> if the cache contains an element with the specified key; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// If the key is found, the item is marked as recently used.
    /// </remarks>
    public bool TryGetValue(TKey key, [NotNullWhen(true)] out TValue? result)
    {
        lock (_lock)
        {
            if (_cacheMap.TryGetValue(key, out var node))
            {
                // Move to front (most recently used)
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                result = node.Value.Value;
                return true;
            }
        }
        result = default;
        return false;
    }

    /// <summary>
    /// Gets an existing value for the specified key if present, or adds a new value created by the valueFactory.
    /// </summary>
    /// <param name="key">The key of the value to get or add.</param>
    /// <param name="valueFactory">The function used to generate a value if the key does not exist.</param>
    /// <returns>The value for the key. This will be either the existing value if the key is already in the cache, or the new value if the key was not in the cache.</returns>
    /// <exception cref="ArgumentNullException">Thrown when the valueFactory or its result is null.</exception>
    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        lock (_lock)
        {
            // Check if key exists
            if (_cacheMap.TryGetValue(key, out var existingNode))
            {
                // Move to front (most recently used)
                _lruList.Remove(existingNode);
                _lruList.AddFirst(existingNode);
                return existingNode.Value.Value;
            }

            // Key doesn't exist, create the value
            TValue value = valueFactory(key);
            ArgumentNullException.ThrowIfNull(value);

            int weight = GetWeight(value);

            // Ensure there's enough space
            EnsureCapacity(weight);

            // Add the new item
            var cacheItem = new CacheItem(key, value, weight);
            var newNode = new LinkedListNode<CacheItem>(cacheItem);
            _lruList.AddFirst(newNode);

            if (_cacheMap.TryAdd(key, newNode))
            {
                _sumWeight += weight;
                return value;
            }

            // If we couldn't add to the dictionary (rare race condition), 
            // remove from linked list and try one more time with the existing value
            _lruList.Remove(newNode);

            if (_cacheMap.TryGetValue(key, out existingNode))
            {
                return existingNode.Value.Value;
            }

            // This should almost never happen - both attempts failed
            throw new InvalidOperationException("Failed to add or retrieve item from cache.");
        }
    }

    /// <summary>
    /// Adds or updates a key-value pair in the cache.
    /// </summary>
    /// <param name="key">The key to add or update.</param>
    /// <param name="value">The value to associate with the key.</param>
    /// <exception cref="ArgumentNullException">Thrown when the value is null.</exception>
    /// <remarks>
    /// If the key already exists, the old value is replaced.
    /// If adding the new item would exceed the cache capacity, least recently used items are removed until there is enough space.
    /// The new or updated item is marked as the most recently used.
    /// </remarks>
    public void Set(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(value);

        int weight = GetWeight(value);

        lock (_lock)
        {
            // If key exists, remove it first
            if (_cacheMap.TryRemove(key, out var existingNode))
            {
                _lruList.Remove(existingNode);
                _sumWeight -= existingNode.Value.Weight;
            }

            // Ensure there's enough space
            EnsureCapacity(weight);

            // Add the new item
            var cacheItem = new CacheItem(key, value, weight);
            var newNode = new LinkedListNode<CacheItem>(cacheItem);
            _lruList.AddFirst(newNode);
            _cacheMap[key] = newNode;
            _sumWeight += weight;
        }
    }

    /// <summary>
    /// Removes all items from the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _lruList.Clear();
            _cacheMap.Clear();
            _sumWeight = 0;
        }
    }

    /// <summary>
    /// Removes the item with the specified key from the cache.
    /// </summary>
    /// <param name="key">The key of the item to remove.</param>
    /// <returns><c>true</c> if the item was found and removed; otherwise, <c>false</c>.</returns>
    public bool Remove(TKey key)
    {
        lock (_lock)
        {
            if (_cacheMap.TryRemove(key, out var node))
            {
                _lruList.Remove(node);
                _sumWeight -= node.Value.Weight;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Gets the weight of a value. By default, each item has a weight of 1.
    /// </summary>
    /// <param name="value">The value to get the weight for.</param>
    /// <returns>The weight of the value.</returns>
    /// <remarks>
    /// Override this method in derived classes to provide custom weight calculations.
    /// </remarks>
    protected virtual int GetWeight(TValue value)
    {
        return 1;
    }

    private void EnsureCapacity(int weight)
    {
        // If a single item is larger than capacity, we can't add it
        if (weight > _capacity)
        {
            throw new ArgumentException($"Item weight ({weight}) exceeds cache capacity ({_capacity}).");
        }

        // Remove least recently used items until we have enough space
        while (_sumWeight + weight > _capacity && _lruList.Last != null)
        {
            var lruNode = _lruList.Last;
            _lruList.RemoveLast();

            if (_cacheMap.TryRemove(lruNode.Value.Key, out _))
            {
                _sumWeight -= lruNode.Value.Weight;
            }
        }
    }
}