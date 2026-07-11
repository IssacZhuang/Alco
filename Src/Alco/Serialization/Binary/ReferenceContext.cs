using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Alco;

public class ReferenceContext
{
    public const string SerializeKey = "$id";

    protected ulong _nextId = 1;
    private readonly ConcurrentDictionary<object, ulong> _objectToId = new();
    private readonly ConcurrentDictionary<ulong, object> _idToObject = new();

    /// <summary>
    /// Gets or assigns a unique ID for the specified object within this context.
    /// Override to customize ID assignment (e.g. cross-context references).
    /// </summary>
    public virtual ulong GetId(object obj)
    {
        return _objectToId.GetOrAdd(obj, AddReference);
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

        _idToObject[id] = obj;
        _objectToId[obj] = id;
    }

    /// <summary>
    /// Attempts to resolve an object by its ID.
    /// Override to add fallback resolution (e.g. cross-context registry).
    /// </summary>
    public virtual bool TryGetReference(ulong id, [NotNullWhen(true)] out object? obj)
    {
        return _idToObject.TryGetValue(id, out obj);
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
