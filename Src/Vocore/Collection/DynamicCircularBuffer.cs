using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Vocore;

/// <summary>
/// A dynamic size circular buffer that grows as needed when cycling through items.
/// </summary>
/// <typeparam name="T">The type of items stored in the buffer</typeparam>
public class DynamicCircularBuffer<T>
{
    /// <summary>
    /// The internal list storing the buffer items
    /// </summary>
    private readonly List<T> _list = new List<T>();

    /// <summary>
    /// Function used to generate new items when the buffer needs to grow
    /// </summary>
    private readonly Func<T> _objectGenerator;

    /// <summary>
    /// The current item in the buffer
    /// </summary>
    private T _current;

    /// <summary>
    /// Index of the current item
    /// </summary>
    private int _usedCount;

    /// <summary>
    /// Gets the total number of items in the buffer
    /// </summary>
    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _list.Count;
    }

    /// <summary>
    /// Gets the item at the specified index
    /// </summary>
    /// <param name="index">The index of the item to get</param>
    /// <returns>The item at the specified index</returns>
    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _list[index];
    }

    /// <summary>
    /// Gets the index of the current item
    /// </summary>
    public int UsedCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _usedCount;
    }


    /// <summary>
    /// Creates a new dynamic circular buffer
    /// </summary>
    /// <param name="objectGenerator">Function used to generate new items when the buffer needs to grow</param>
    public DynamicCircularBuffer(Func<T> objectGenerator)
    {
        _objectGenerator = objectGenerator;
        _usedCount = 0;
        _list.Add(_current = _objectGenerator());
    }

    /// <summary>
    /// Moves to and returns the next item in the buffer.
    /// If the current index exceeds the buffer size, a new item will be generated and added.
    /// </summary>
    /// <returns>The next item in the buffer</returns>
    public T Next()
    {
        if (_usedCount >= _list.Count)
        {
            _list.Add(_current = _objectGenerator());
        }
        _current = _list[_usedCount++];
        return _current;
    }

    /// <summary>
    /// Resets the buffer position to the first item
    /// </summary>
    public void ResetToHead()
    {
        _usedCount = 0;
        _current = _list[0];
    }
}
