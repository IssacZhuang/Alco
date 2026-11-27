using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Alco;

internal readonly struct PackedTable
{
    private const int LinearLookupLimit = 16;
    private readonly ReadOnlyMemory<byte> _fields;
    private readonly int _fieldCount;
    private readonly Dictionary<int, int>? _offsets;

    private PackedTable(ReadOnlyMemory<byte> fields, int fieldCount, int depth, Dictionary<int, int>? offsets)
    {
        _fields = fields;
        _fieldCount = fieldCount;
        Depth = depth;
        _offsets = offsets;
    }

    public int Depth { get; }

    public static PackedTable Empty(int depth) => new(default, 0, depth, null);

    public static bool TryOpen(ReadOnlyMemory<byte> data, PackedStringTable.Reader strings, int depth, out PackedTable table)
    {
        table = default;
        var cursor = new PackedCursor(data);
        if (depth > PackedWire.MaxDepth || !cursor.TryReadVarUInt(out int count) || count > cursor.Remaining / 2)
            return false;

        ReadOnlyMemory<byte> fields = data[cursor.Position..];
        cursor = new PackedCursor(fields);
        Dictionary<int, int>? offsets = count > LinearLookupLimit ? new() : null;
        Span<int> keys = stackalloc int[LinearLookupLimit];
        for (int i = 0; i < count; i++)
        {
            if (!cursor.TryReadId(strings.Count, out int id))
                return false;

            int offset = cursor.Position;
            if (!cursor.TryReadByte(out byte tag) || !PackedWire.TrySkipPayload(ref cursor, (PackedTag)tag, strings.Count) || !strings.RegisterKey(id))
                return false;

            if (offsets is not null)
            {
                if (!offsets.TryAdd(id, offset))
                    return false;
                continue;
            }
            if (keys[..i].Contains(id))
                return false;
            keys[i] = id;
        }
        if (cursor.Remaining != 0)
            return false;

        table = new PackedTable(fields, count, depth, offsets);
        return true;
    }

    public bool TryGetField(string key, PackedStringTable.Reader strings, out PackedTag tag, out PackedCursor payload)
    {
        tag = default;
        payload = default;
        if (_fieldCount == 0 || !strings.TryGetKeyId(key, out int keyId))
            return false;

        if (_offsets is not null)
        {
            if (!_offsets.TryGetValue(keyId, out int offset))
                return false;

            tag = (PackedTag)_fields.Span[offset];
            payload = new PackedCursor(_fields[(offset + 1)..]);
            return true;
        }

        var cursor = new PackedCursor(_fields);
        for (int i = 0; i < _fieldCount; i++)
        {
            cursor.TryReadVarUInt(out int id);
            cursor.TryReadByte(out byte currentTag);
            if (id == keyId)
            {
                tag = (PackedTag)currentTag;
                payload = cursor;
                return true;
            }
            PackedWire.TrySkipPayload(ref cursor, (PackedTag)currentTag, strings.Count);
        }
        return false;
    }

    public bool TryGetChildTableBytes(string key, PackedStringTable.Reader strings, out ReadOnlyMemory<byte> body)
    {
        body = default;
        return TryGetField(key, strings, out PackedTag tag, out PackedCursor payload) &&
            tag == PackedTag.Table && payload.TryReadLengthPrefixed(out body);
    }

    public bool TryGetTableArray(string key, PackedStringTable.Reader strings, out PackedCursor elements, out int count)
    {
        count = 0;
        return TryGetField(key, strings, out PackedTag tag, out elements) && tag == PackedTag.TableArray &&
            elements.TryReadVarUInt(out count);
    }
}

internal readonly record struct PackedMapEntry(int KeyId, int StringId, ReadOnlyMemory<byte> Bytes);

internal struct PackedMapCursor
{
    private PackedCursor _cursor;
    private readonly int _stringCount;
    private int _remaining;

    private PackedMapCursor(PackedCursor cursor, int count, PackedMapKind kind, int stride, int stringCount)
    {
        _cursor = cursor;
        _remaining = count;
        Count = count;
        Kind = kind;
        Stride = stride;
        _stringCount = stringCount;
    }

    public int Count { get; }
    public PackedMapKind Kind { get; }
    public int Stride { get; }

    public static bool TryOpen(PackedCursor payload, int stringCount, out PackedMapCursor map)
    {
        map = default;
        if (!payload.TryReadLengthPrefixed(out ReadOnlyMemory<byte> bytes))
            return false;

        var cursor = new PackedCursor(bytes);
        if (!cursor.TryReadVarUInt(out int count) || !cursor.TryReadByte(out byte kind) || kind > (byte)PackedMapKind.Table)
            return false;

        int stride = 0;
        if (kind == (byte)PackedMapKind.Unmanaged && (!cursor.TryReadVarUInt(out stride) || stride == 0))
            return false;
        if (count > cursor.Remaining / (Math.Max(1, stride) + 1L) || (count == 0 && cursor.Remaining != 0))
            return false;

        map = new PackedMapCursor(cursor, count, (PackedMapKind)kind, stride, stringCount);
        return true;
    }

    public bool TryReadNext(out PackedMapEntry entry)
    {
        entry = default;
        if (_remaining == 0 || !_cursor.TryReadId(_stringCount, out int key))
            return false;

        int stringId = 0;
        ReadOnlyMemory<byte> bytes = default;
        bool valid = Kind switch
        {
            PackedMapKind.Unmanaged => _cursor.TryRead(Stride, out bytes),
            PackedMapKind.StringId => _cursor.TryReadId(_stringCount, out stringId),
            _ => _cursor.TryReadLengthPrefixed(out bytes),
        };
        if (!valid || (_remaining == 1 && _cursor.Remaining != 0))
            return false;

        _remaining--;
        entry = new PackedMapEntry(key, stringId, bytes);
        return true;
    }
}

internal sealed class PackedReadSession(PackedStringTable.Reader strings, ReferenceContext? referenceContext, PackedErrors errors)
{
    private ConcurrentDictionary<ReadOnlyMemory<byte>, byte>? _failedItems;
    private ConditionalWeakTable<object, Dictionary<ReadOnlyMemory<byte>, object>>? _loadedCollections;

    public PackedStringTable.Reader Strings { get; } = strings;
    public ReferenceContext? ReferenceContext { get; } = referenceContext;
    public PackedErrors Errors { get; } = errors;

    public bool TryOpenChild(PackedTable parent, ReadOnlyMemory<byte> body, string key, out PackedTable child)
    {
        if (PackedTable.TryOpen(body, Strings, parent.Depth + 1, out child))
            return true;

        if (MarkFailed(body))
            Errors.Report($"Invalid PackedBinary table for '{key}'.");
        return false;
    }

    public bool MarkFailed(ReadOnlyMemory<byte> body) =>
        (_failedItems ?? LazyInitializer.EnsureInitialized(ref _failedItems, static () => new())).TryAdd(body, 0);

    public bool WasFailed(ReadOnlyMemory<byte> body) => _failedItems?.ContainsKey(body) == true;

    public void RecordCollection<T>(ICollection<T> collection, ReadOnlyMemory<byte> origin, List<(T Item, ReadOnlyMemory<byte> Body)> items)
    {
        (_loadedCollections ??= new()).GetOrCreateValue(collection)[origin] = items;
    }

    public List<(T Item, ReadOnlyMemory<byte> Body)>? GetCollectionItems<T>(ICollection<T> collection, ReadOnlyMemory<byte> origin)
    {
        if (_loadedCollections is null || !_loadedCollections.TryGetValue(collection, out var bindings) || !bindings.TryGetValue(origin, out object? items))
            return null;

        return items as List<(T Item, ReadOnlyMemory<byte> Body)>;
    }

    public bool TryGetMap(PackedTable table, string key, PackedMapKind kind, out PackedMapCursor map)
    {
        map = default;
        if (!table.TryGetField(key, Strings, out PackedTag tag, out PackedCursor payload) || tag != PackedTag.Map)
            return false;
        if (PackedMapCursor.TryOpen(payload, Strings.Count, out map))
            return map.Kind == kind;

        Errors.Report($"Invalid PackedBinary map for '{key}'.");
        return false;
    }

    public bool TryReadMapEntry(ref PackedMapCursor map, string key, out PackedMapEntry item)
    {
        if (map.TryReadNext(out item))
            return true;

        Errors.Report($"Invalid PackedBinary map entry for '{key}'.");
        return false;
    }
}
