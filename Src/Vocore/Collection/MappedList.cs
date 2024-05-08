using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Vocore;

/// <summary>
/// Represents a list of elements that can be accessed by a key.
/// </summary>
/// <typeparam name="TKey">The type of the key used to map elements.</typeparam>
/// <typeparam name="TValue">The type of elements in the list.</typeparam>
public class MappedList<TKey,TValue> : IEnumerable<TValue> where TKey : notnull
{
    private readonly List<TValue> _list = new();
    private readonly Dictionary<TKey, TValue> _map = new();

    /// <summary>
    /// Gets the number of elements in the list.
    /// </summary>
    public int Count => _list.Count;

    public bool IsReadOnly
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => false;
    }

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get or set.</param>
    /// <returns>The element at the specified index.</returns>
    public TValue this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _list[index];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _list[index] = value;
    }

    /// <summary>
    /// Adds an element to the list.
    /// </summary>
    /// <param name="key">The key of the element to add.</param>
    /// <param name="value">The element to add.</param>
    /// <exception cref="ArgumentException">Thrown when the element already exists in the list.</exception>
    public void Add(TKey key, TValue value)
    {
        if (_map.TryAdd(key, value))
        {
            _list.Add(value);
        }
        else
        {
            throw new ArgumentException($"The name {key} already exists in the list.");
        }
    }

    /// <summary>
    /// Tries to add an element to the list.
    /// </summary>
    /// <param name="key">The key of the element to add.</param>
    /// <param name="value">The element to add.</param>
    /// <returns><c>true</c> if the element was added successfully; otherwise, <c>false</c>.</returns>
    public bool TryAdd(TKey key, TValue value)
    {
        if (_map.TryAdd(key, value))
        {
            _list.Add(value);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes an element from the list by its key.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    /// <exception cref="ArgumentException">Thrown when the element does not exist in the list.</exception>
    public void Remove(TKey key)
    {
        if (_map.Remove(key, out var value))
        {
            _list.Remove(value);
        }
        else
        {
            throw new ArgumentException($"The key '{key}' does not exist in the list.");
        }
    }

    /// <summary>
    /// Tries to remove an element from the list by its key.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    /// <returns><c>true</c> if the element was removed successfully; otherwise, <c>false</c>.</returns>
    public bool TryRemove(TKey key)
    {
        if (_map.Remove(key, out var value))
        {
            _list.Remove(value);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Gets the element with the specified key.
    /// </summary>
    /// <param name="key">The key of the element to get.</param>
    /// <returns>The element with the specified key.</returns>
    /// <exception cref="ArgumentException">Thrown when the element does not exist in the list.</exception>
    public TValue Get(TKey key)
    {
        if (_map.TryGetValue(key, out var value))
        {
            return value;
        }
        throw new ArgumentException($"The name {key} does not exist in the list.");
    }

    /// <summary>
    /// Tries to get the element with the specified key.
    /// </summary>
    /// <param name="key">The key of the element to get.</param>
    /// <param name="value">When this method returns, contains the element with the specified key, if found; otherwise, <c>null</c>.</param>
    /// <returns><c>true</c> if the element with the specified element was found; otherwise, <c>false</c>.</returns>
    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue? value)
    {
        return _map.TryGetValue(key, out value);
    }

    public IEnumerator<TValue> GetEnumerator()
    {
        return _list.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}