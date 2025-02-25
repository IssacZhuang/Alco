using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

/// <summary>
/// Implements a Least Recently Used (LRU) cache with weighted capacity management.
/// <br/> This cache is not thread-safe, use <see cref="ConcurrentLruCache{TKey, TValue}"/> for thread-safe operations.
/// </summary>
/// <typeparam name="TKey">The type of keys in the cache. Must be non-null.</typeparam>
/// <typeparam name="TValue">The type of values in the cache. Must be a reference type.</typeparam>
/// <remarks>
/// This cache automatically removes the least recently used items when it reaches capacity.
/// Each item can have a custom weight, allowing for more flexible capacity management.
/// </remarks>
public class LruCache<TKey, TValue> where TKey : notnull where TValue : class
{

    private struct CacheItem
    {
        public TKey key;

        public TValue value;

        public int weight;

        public CacheItem(TKey key, TValue value, int weight)
        {
            this.key = key;
            this.value = value;
            this.weight = weight;
        }
    }

    /// <summary>
    /// Dictionary that maps keys to their corresponding nodes in the linked list for O(1) lookups.
    /// </summary>
    private readonly Dictionary<TKey, LinkedListNode<CacheItem>> _index = new Dictionary<TKey, LinkedListNode<CacheItem>>();

    /// <summary>
    /// Linked list that maintains the order of items based on their usage, with least recently used at the front.
    /// </summary>
    private readonly LinkedList<CacheItem> _leastRecentList = new LinkedList<CacheItem>();

    /// <summary>
    /// The maximum capacity of the cache, measured in total weight units.
    /// </summary>
    private readonly int _capacity;

    /// <summary>
    /// The current sum of all item weights in the cache.
    /// </summary>
    private int _sumWeight = 0;

    /// <summary>
    /// Gets the number of items currently in the cache.
    /// </summary>
    public int Count => _index.Count;

    /// <summary>
    /// Gets the maximum capacity of the cache, measured in weight units.
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// Gets the current total weight of all items in the cache.
    /// </summary>
    public int SumWeight => _sumWeight;

    /// <summary>
    /// Initializes a new instance of the <see cref="LruCache{Tkey, TValue}"/> class.
    /// </summary>
    /// <param name="capacity">The maximum capacity of the cache, measured in weight units.</param>
    public LruCache(int capacity)
    {
        _capacity = capacity;
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
            if (_index.TryGetValue(key, out var node))
            {
                WasUsed(node);
                return node.Value.value;
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
        if (_index.TryGetValue(key, out var value))
        {
            result = value.Value.value;
            WasUsed(value);
            return true;
        }
        result = default;
        return false;
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

        if (_index.TryGetValue(key, out var existingNode))
        {
            _leastRecentList.Remove(existingNode);
            _index.Remove(key);
            _sumWeight -= existingNode.Value.weight;
        }

        int weight = GetWeight(value);
        if (_sumWeight + weight > _capacity)
        {
            RemoveLeastUsed();
        }
        LinkedListNode<CacheItem> linkedListNode = new LinkedListNode<CacheItem>(new CacheItem(key, value, weight));
        _index.Add(key, linkedListNode);
        _leastRecentList.AddLast(linkedListNode);
        _sumWeight += weight;
    }

    /// <summary>
    /// Removes all items from the cache.
    /// </summary>
    public void Clear()
    {
        _index.Clear();
        _leastRecentList.Clear();
        _sumWeight = 0;
    }

    /// <summary>
    /// Removes the item with the specified key from the cache.
    /// </summary>
    /// <param name="key">The key of the item to remove.</param>
    /// <returns><c>true</c> if the item was found and removed; otherwise, <c>false</c>.</returns>
    public bool Remove(TKey key)
    {
        if (_index.TryGetValue(key, out var node))
        {
            _leastRecentList.Remove(node);
            _index.Remove(key);
            _sumWeight -= node.Value.weight;
            return true;
        }
        return false;
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

    private void WasUsed(LinkedListNode<CacheItem> node)
    {
        _leastRecentList.Remove(node);
        _leastRecentList.AddLast(node);
    }

    private void RemoveLeastUsed()
    {
        LinkedListNode<CacheItem>? first = _leastRecentList.First;
        if (first != null)
        {
            _leastRecentList.RemoveFirst();
            _index.Remove(first.Value.key);
            _sumWeight -= first.Value.weight;
        }
    }
}
