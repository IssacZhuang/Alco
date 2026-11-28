using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Alco;

/// <summary>
/// Assigns and resolves unique identifiers for referenceable objects during serialization.
/// </summary>
public class ReferenceContext
{
    /// <summary>The reserved serialization field key for object reference identifiers.</summary>
    public const string SerializeKey = "$id";

    protected ulong _nextId = 1;
    private ConcurrentDictionary<object, ulong>? _objectToId;
    private ConcurrentDictionary<ulong, object>? _idToObject;

    private ConcurrentDictionary<object, ulong> ObjectToId =>
        LazyInitializer.EnsureInitialized(ref _objectToId, static () => new ConcurrentDictionary<object, ulong>());

    private ConcurrentDictionary<ulong, object> IdToObject =>
        LazyInitializer.EnsureInitialized(ref _idToObject, static () => new ConcurrentDictionary<ulong, object>());

    /// <summary>
    /// Gets or assigns the unique identifier of the specified object within this context; override to customize ID assignment.
    /// </summary>
    public virtual ulong GetId(object obj)
    {
        return ObjectToId.GetOrAdd(obj, AddReference);
    }

    /// <summary>
    /// Registers an object with the specified ID for later resolution.
    /// Override to route registration through a shared registry (e.g. cross-context),
    /// keeping the object→id table consistent with <see cref="GetId"/>'s allocation source.
    /// </summary>
    public virtual void SetReference(ulong id, object obj)
    {
        if (id == 0)
            return;

        IdToObject[id] = obj;
        ObjectToId[obj] = id;
    }

    /// <summary>
    /// Attempts to resolve an object by its ID.
    /// Override to add fallback resolution (e.g. cross-context registry).
    /// </summary>
    public virtual bool TryGetReference(ulong id, [NotNullWhen(true)] out object? obj)
    {
        ConcurrentDictionary<ulong, object>? idToObject = Volatile.Read(ref _idToObject);
        if (idToObject is null)
        {
            obj = null;
            return false;
        }

        return idToObject.TryGetValue(id, out obj);
    }

    private ulong AddReference(object obj)
    {
        return Interlocked.Increment(ref _nextId);
    }

    /// <summary>
    /// Records that an object is the target of a <c>BindReference</c> call.
    /// Subclasses use this to track which objects should have their IDs persisted.
    /// </summary>
    public virtual void TrackReferenced(object obj) { }
}
