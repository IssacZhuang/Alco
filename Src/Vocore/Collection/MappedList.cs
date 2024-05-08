using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Vocore;

/// <summary>
/// Represents a list that maps elements by their name.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
public class MappedList<T> : IEnumerable<T> where T : INameable
{
    private readonly List<T> _list = new();
    private readonly Dictionary<string, T> _map = new();

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
    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _list[index];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _list[index] = value;
    }

    /// <summary>
    /// Adds an element to the list.
    /// </summary>
    /// <param name="value">The element to add.</param>
    /// <exception cref="ArgumentException">Thrown when the name of the element already exists in the list.</exception>
    public void Add(T value)
    {
        if (_map.ContainsKey(value.Name))
        {
            throw new ArgumentException($"The name {value.Name} already exists in the list.");
        }
        _list.Add(value);
        _map.Add(value.Name, value);
    }

    /// <summary>
    /// Tries to add an element to the list.
    /// </summary>
    /// <param name="value">The element to add.</param>
    /// <returns><c>true</c> if the element was added successfully; otherwise, <c>false</c>.</returns>
    public bool TryAdd(T value)
    {
        if (_map.ContainsKey(value.Name))
        {
            return false;
        }
        _list.Add(value);
        _map.Add(value.Name, value);
        return true;
    }

    /// <summary>
    /// Removes an element from the list by its name.
    /// </summary>
    /// <param name="key">The name of the element to remove.</param>
    /// <exception cref="ArgumentException">Thrown when the name of the element does not exist in the list.</exception>
    public void Remove(string key)
    {
        if (!_map.TryGetValue(key, out var value))
        {
            throw new ArgumentException($"The name {key} does not exist in the list.");
        }
        _list.Remove(value);
        _map.Remove(key);
    }

    /// <summary>
    /// Tries to remove an element from the list by its name.
    /// </summary>
    /// <param name="key">The name of the element to remove.</param>
    /// <returns><c>true</c> if the element was removed successfully; otherwise, <c>false</c>.</returns>
    public bool TryRemove(string key)
    {
        if (!_map.TryGetValue(key, out var value))
        {
            return false;
        }
        _list.Remove(value);
        _map.Remove(key);
        return true;
    }

    /// <summary>
    /// Removes an element from the list.
    /// </summary>
    /// <param name="value">The element to remove.</param>
    public void Remove(T value)
    {
        _list.Remove(value);
        _map.Remove(value.Name);
    }

    /// <summary>
    /// Gets the element with the specified name.
    /// </summary>
    /// <param name="key">The name of the element to get.</param>
    /// <returns>The element with the specified name.</returns>
    /// <exception cref="ArgumentException">Thrown when the name of the element does not exist in the list.</exception>
    public T Get(string key)
    {
        if (_map.TryGetValue(key, out var value))
        {
            return value;
        }
        throw new ArgumentException($"The name {key} does not exist in the list.");
    }

    /// <summary>
    /// Tries to get the element with the specified name.
    /// </summary>
    /// <param name="key">The name of the element to get.</param>
    /// <param name="value">When this method returns, contains the element with the specified name, if found; otherwise, <c>null</c>.</param>
    /// <returns><c>true</c> if the element with the specified name was found; otherwise, <c>false</c>.</returns>
    public bool TryGet(string key, [NotNullWhen(true)] out T? value)
    {
        return _map.TryGetValue(key, out value);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _list.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}