using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

/// <summary>
/// Represents a thread-safe object pool that allows concurrent access to a collection of reusable objects.
/// </summary>
/// <typeparam name="T">The type of objects stored in the pool.</typeparam>
public class ConcurrentPool<T> : IEnumerable<T>
{
    private readonly ConcurrentBag<T> _objects;
    private readonly Func<T> _objectGenerator;

    /// <summary>
    /// Gets the number of objects currently available in the pool.
    /// </summary>
    public int Count => _objects.Count;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentPool{T}"/> class.
    /// </summary>
    /// <param name="objectGenerator">A function that creates new objects when the pool is empty.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="objectGenerator"/> is null.</exception>
    public ConcurrentPool(Func<T> objectGenerator)
    {
        _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
        _objects = new ConcurrentBag<T>();
    }

    /// <summary>
    /// Gets an object from the pool or creates a new one if the pool is empty.
    /// </summary>
    /// <returns>An object of type <typeparamref name="T"/>.</returns>
    public T Get() => _objects.TryTake(out T? item) ? item : _objectGenerator();

    /// <summary>
    /// Attempts to get an object from the pool without creating a new one.
    /// </summary>
    /// <param name="item">When this method returns, contains the object from the pool if available; otherwise, the default value.</param>
    /// <returns>true if an object was removed from the pool; otherwise, false.</returns>
    public bool TryTake([MaybeNullWhen(false)] out T item) => _objects.TryTake(out item);

    /// <summary>
    /// Returns an object to the pool.
    /// </summary>
    /// <param name="item">The object to return to the pool.</param>
    public void Return(T item) => _objects.Add(item);

    /// <summary>
    /// Returns an enumerator that iterates through the objects in the pool.
    /// </summary>
    /// <returns>An enumerator for the objects in the pool.</returns>
    public IEnumerator<T> GetEnumerator() => _objects.GetEnumerator();

    /// <summary>
    /// Removes all objects from the pool.
    /// </summary>
    public void Clear() => _objects.Clear();

    /// <summary>
    /// Returns an enumerator that iterates through the objects in the pool.
    /// </summary>
    /// <returns>An enumerator for the objects in the pool.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}