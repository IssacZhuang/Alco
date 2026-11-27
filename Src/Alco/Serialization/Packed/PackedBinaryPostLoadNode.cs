using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Alco;

/// <summary>
/// Revisits loaded PackedBinary objects to resolve their references.
/// </summary>
internal sealed class PackedBinaryPostLoadNode : SerializeNode
{
    private readonly PackedTable _table;
    private readonly PackedReadSession _session;
    private readonly bool _clearMissingReferences;

    public PackedBinaryPostLoadNode(PackedTable table, PackedReadSession session, bool clearMissingReferences)
    {
        _table = table;
        _session = session;
        _clearMissingReferences = clearMissingReferences;
    }

    /// <inheritdoc/>
    public override ReferenceContext? ReferenceContext => _session.ReferenceContext;

    /// <inheritdoc/>
    public override void BindValue<T>(string key, ref T value, T @default = default) { }

    /// <inheritdoc/>
    public override void BindValue<T>(string key, ref T? value) { }

    /// <inheritdoc/>
    public override void BindEnum<T>(string key, ref T value, T @default = default) { }

    /// <inheritdoc/>
    public override void BindString(string key, ref string value, string @default = "") { }

    /// <inheritdoc/>
    public override void BindBinary(string key, ref ReadOnlyMemory<byte> data) { }

    /// <inheritdoc/>
    public override void BindMemory<T>(string key, Span<T> memory) { }

    /// <inheritdoc/>
    public override void BindCollection<T>(string key, ICollection<T> value) { }

    /// <inheritdoc/>
    public override void BindCollection(string key, ICollection<string> value) { }

    /// <inheritdoc/>
    public override void BindDictionary<TValue>(string key, IDictionary<string, TValue> value) { }

    /// <inheritdoc/>
    public override void BindDictionary(string key, IDictionary<string, string> value) { }

    /// <inheritdoc/>
    public override void BindDictionary(string key, IDictionary<string, ReadOnlyMemory<byte>> value) { }

    /// <inheritdoc/>
    public override void BindSerializable<T>(string key, T value)
    {
        T? item = value;
        if (_table.TryGetChildTableBytes(key, _session.Strings, out ReadOnlyMemory<byte> body))
        {
            ReplayChild<T>(key, body, ref item);
            return;
        }
        if (_clearMissingReferences && _table.Depth < PackedWire.MaxDepth)
            Replay<T>(key, PackedTable.Empty(_table.Depth + 1), ref item);
    }

    /// <inheritdoc/>
    public override void BindSerializableOptional<T>(string key, ref T? value, Func<SerializeReadNode, T> onCreate) where T : default
    {
        if (value is null)
            return;

        if (_table.TryGetChildTableBytes(key, _session.Strings, out ReadOnlyMemory<byte> body))
            ReplayChild<T>(key, body, ref value);
        else if (_clearMissingReferences)
            value = default;
    }

    /// <inheritdoc/>
    public override void BindArraySerializable<T>(string key, IReadOnlyList<T> value)
    {
        if (!_table.TryGetTableArray(key, _session.Strings, out PackedCursor elements, out int count))
            return;

        int length = Math.Min(value.Count, count);
        for (int i = 0; i < length && elements.TryReadLengthPrefixed(out ReadOnlyMemory<byte> body); i++)
        {
            T? item = value[i];
            ReplayChild<T>(key, body, ref item);
        }
    }

    /// <inheritdoc/>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value) => ReplayCollection(key, value);

    /// <inheritdoc/>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value, Func<SerializeReadNode, T> onCreate) =>
        ReplayCollection(key, value);

    /// <inheritdoc/>
    public override void BindDictionarySerializable<T>(string key, IDictionary<string, T> value) => ReplayMap(key, value);

    /// <inheritdoc/>
    public override void BindDictionarySerializable<T>(string key, IDictionary<string, T> value, Func<SerializeReadNode, T> onCreate) =>
        ReplayMap(key, value);

    /// <inheritdoc/>
    public override void BindReference<T>(string key, ref T? referenceable) where T : default
    {
        ReferenceContext? context = ReferenceContext;
        if (context is null)
            return;

        if (_clearMissingReferences)
            referenceable = default;
        if (!_table.TryGetField(key, _session.Strings, out PackedTag tag, out PackedCursor payload) ||
            tag != PackedTag.Primitive8 || !payload.TryRead(sizeof(ulong), out ReadOnlyMemory<byte> bytes))
            return;

        ulong id = MemoryMarshal.Read<ulong>(bytes.Span);
        if (id == 0)
            return;
        if (context.TryGetReference(id, out object? target) && target is T reference)
        {
            referenceable = reference;
            return;
        }
        _session.Errors.Report($"Failed to resolve PackedBinary reference '{key}': {id}.");
    }

    private void ReplayChild<T>(string key, ReadOnlyMemory<byte> body, ref T? value) where T : ISerializable
    {
        if (!_session.WasFailed(body) && _session.TryOpenChild(_table, body, key, out PackedTable child))
            Replay(key, child, ref value);
    }

    private void Replay<T>(string key, PackedTable table, ref T? value) where T : ISerializable
    {
        try
        {
            value!.OnSerialize(new PackedBinaryPostLoadNode(table, _session, _clearMissingReferences), SerializeMode.PostLoad);
        }
        catch (Exception ex) when (_session.Errors.CanReport(ex))
        {
            _session.Errors.Report($"Failed to post-load PackedBinary object for '{key}': {ex}");
        }
    }

    private void ReplayCollection<T>(string key, ICollection<T> value) where T : ISerializable
    {
        if (!_table.TryGetTableArray(key, _session.Strings, out PackedCursor elements, out int count))
            return;

        List<(T Item, ReadOnlyMemory<byte> Body)>? loaded = _session.GetCollectionItems(value, elements.Unread);
        if (loaded is not null)
        {
            for (int i = 0; i < loaded.Count; i++)
            {
                (T? item, ReadOnlyMemory<byte> body) = loaded[i];
                ReplayChild<T>(key, body, ref item);
            }
            return;
        }

        using IEnumerator<T> items = value.GetEnumerator();
        for (int i = 0; i < count && elements.TryReadLengthPrefixed(out ReadOnlyMemory<byte> body); i++)
        {
            if (_session.WasFailed(body))
                continue;
            if (!items.MoveNext())
                break;

            T? item = items.Current;
            ReplayChild<T>(key, body, ref item);
        }
    }

    private void ReplayMap<T>(string key, IDictionary<string, T> value) where T : ISerializable
    {
        if (!_session.TryGetMap(_table, key, PackedMapKind.Table, out PackedMapCursor map))
            return;

        for (int i = 0; i < map.Count && _session.TryReadMapEntry(ref map, key, out PackedMapEntry entry); i++)
        {
            string name = _session.Strings.GetString(entry.KeyId);
            if (value.TryGetValue(name, out T? item))
                ReplayChild<T>(key, entry.Bytes, ref item);
        }
    }
}
