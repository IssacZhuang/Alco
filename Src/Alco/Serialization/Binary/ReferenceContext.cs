using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco;

public sealed class ReferenceContext
{
    public const string SerializeKey = "$id";

    private uint _nextId = 0;
    private readonly Dictionary<object, uint> _objectToId = new();
    private readonly Dictionary<uint, object> _idToObject = new();

    public uint GetId(object obj)
    {
        if (!_objectToId.TryGetValue(obj, out uint id))
        {
            id = _nextId++;
            _objectToId[obj] = id;
        }
        return id;
    }

    public void SetReference(uint id, object obj)
    {
        _idToObject[id] = obj;
    }

    public bool TryGetReference(uint id, [NotNullWhen(true)] out object? obj)
    {
        return _idToObject.TryGetValue(id, out obj);
    }
}