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

    // --- Two-pass selective reference stamping hooks ---

    /// <summary>
    /// Records that an <see cref="IReferenceable"/> object has been serialized
    /// into the given <see cref="BinaryTable"/> content. Subclasses use this
    /// to build a mapping for deferred <c>$id</c> stamping.
    /// </summary>
    public virtual void TrackNode(object obj, BinaryTable content) { }

    /// <summary>
    /// Records that an object is the target of a <c>BindReference</c> call.
    /// Only tracked objects will receive a <c>$id</c> stamp during
    /// <see cref="StampReferencedIds"/>.
    /// </summary>
    public virtual void TrackReferenced(object obj) { }

    /// <summary>
    /// Stamps <c>$id</c> on all tracked content whose objects have been referenced.
    /// Override to implement the deferred stamping logic.
    /// Safe to call multiple times — implementations must handle duplicate calls.
    /// </summary>
    public virtual void StampReferencedIds() { }
}
