using System;
using System.Collections.Concurrent;

namespace Vocore;

public class ConcurrentPool<T>
{
    private readonly ConcurrentBag<T> _objects;
    private readonly Func<T> _objectGenerator;

    public ConcurrentPool(Func<T> objectGenerator)
    {
        _objectGenerator = objectGenerator ?? throw new ArgumentNullException(nameof(objectGenerator));
        _objects = new ConcurrentBag<T>();
    }

    public T Get() => _objects.TryTake(out T? item) ? item : _objectGenerator();

    public void Return(T item) => _objects.Add(item);
}