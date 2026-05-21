using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Alco;

public class ReferenceContext
{
    public const string SerializeKey = "$id";

    protected uint _nextId = 1;
    private readonly ConcurrentDictionary<object, uint> _objectToId = new();
    private readonly ConcurrentDictionary<uint, object> _idToObject = new();

    /// <summary>
    /// Gets or assigns a unique ID for the specified object within this context.
    /// Override to customize ID assignment (e.g. cross-context references).
    /// </summary>
    public virtual uint GetId(object obj)
    {
        return _objectToId.GetOrAdd(obj, AddReference);
    }

    /// <summary>
    /// Registers an object with the specified ID for later resolution.
    /// </summary>
    public void SetReference(uint id, object obj)
    {
        if (id == 0)
            return;

        _idToObject[id] = obj;
        _objectToId[obj] = id;
    }

    /// <summary>
    /// Attempts to resolve an object by its ID.
    /// Override to add fallback resolution (e.g. cross-context registry).
    /// </summary>
    public virtual bool TryGetReference(uint id, [NotNullWhen(true)] out object? obj)
    {
        return _idToObject.TryGetValue(id, out obj);
    }

    /// <summary>
    /// Checks if an object already has an assigned ID, without creating one.
    /// </summary>
    protected bool TryGetExistingId(object obj, out uint id)
    {
        return _objectToId.TryGetValue(obj, out id);
    }

    /// <summary>
    /// Caches an ID for an object without incrementing the local counter.
    /// </summary>
    protected void CacheId(object obj, uint id)
    {
        _objectToId[obj] = id;
    }

    private uint AddReference(object obj)
    {
        return Interlocked.Increment(ref _nextId);
    }
}
