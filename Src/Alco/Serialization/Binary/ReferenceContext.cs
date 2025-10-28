using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Alco;

public sealed class ReferenceContext
{
    public const string SerializeKey = "$id";

    private uint _nextId = 0;
    private readonly ConcurrentDictionary<object, uint> _objectToId = new();
    private readonly ConcurrentDictionary<uint, object> _idToObject = new();

    public uint GetId(object obj)
    {
        return _objectToId.GetOrAdd(obj, AddReference);
    }

    public void SetReference(uint id, object obj)
    {
        _idToObject[id] = obj;
    }

    public bool TryGetReference(uint id, [NotNullWhen(true)] out object? obj)
    {
        return _idToObject.TryGetValue(id, out obj);
    }

    private uint AddReference(object obj)
    {
        return Interlocked.Increment(ref _nextId);
    }
}