using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco;

/// <summary>
/// Reads named fields from a PackedBinary table without consuming them.
/// </summary>
public sealed class PackedBinaryReadNode : SerializeReadNode
{
    private readonly PackedTable _table;
    private readonly PackedReadSession _session;

    private PackedBinaryReadNode(PackedTable table, PackedReadSession session)
    {
        _table = table;
        _session = session;
    }

    /// <inheritdoc/>
    public override ReferenceContext? ReferenceContext => _session.ReferenceContext;

    /// <summary>
    /// Opens the root table of a PackedBinary document without creating a target object.
    /// </summary>
    /// <param name="data">The document, which must remain immutable while its views are in use.</param>
    /// <param name="node">The root read node on success.</param>
    /// <param name="refCtx">The optional context used to register and resolve object identities.</param>
    /// <param name="onError">The optional error callback.</param>
    /// <returns>Whether the document header, root table and string table are valid.</returns>
    public static bool TryOpen(ReadOnlyMemory<byte> data, [NotNullWhen(true)] out PackedBinaryReadNode? node,
        ReferenceContext? refCtx = null, Action<string>? onError = null)
    {
        PackedWire.RequireLittleEndian();
        var errors = new PackedErrors(onError);
        node = null;
        if (!PackedWire.TryReadHeader(data.Span, out int stringOffset, out int stringCount) ||
            !PackedStringTable.Reader.TryOpen(data[stringOffset..], stringCount, out PackedStringTable.Reader? strings) ||
            !PackedTable.TryOpen(data[PackedWire.HeaderSize..stringOffset], strings, 0, out PackedTable table))
        {
            errors.Report("Invalid PackedBinary document header, root table, or string table.");
            return false;
        }

        node = new PackedBinaryReadNode(table, new PackedReadSession(strings, refCtx, errors));
        return true;
    }

    /// <summary>
    /// Creates a post-load node over this table and its current load session.
    /// </summary>
    /// <param name="clearMissingReferences">Whether to clear stale references on reused objects.</param>
    /// <returns>The node for the reference-resolution pass.</returns>
    public SerializeNode CreatePostLoadNode(bool clearMissingReferences = false) =>
        new PackedBinaryPostLoadNode(_table, _session, clearMissingReferences);

    /// <inheritdoc/>
    public override bool TryGetChildNode(string key, [NotNullWhen(true)] out SerializeReadNode? childNode)
    {
        childNode = null;
        if (!_table.TryGetChildTableBytes(key, _session.Strings, out ReadOnlyMemory<byte> body) ||
            !_session.TryOpenChild(_table, body, key, out PackedTable child))
            return false;

        childNode = new PackedBinaryReadNode(child, _session);
        return true;
    }

    /// <inheritdoc/>
    public override T GetValue<T>(string key, T @default = default)
    {
        int size = Unsafe.SizeOf<T>();
        if (!TryGetField(key, out PackedTag tag, out PackedCursor payload) || tag != PackedWire.GetScalarTag(size))
            return @default;

        ReadOnlyMemory<byte> bytes;
        if (tag == PackedTag.Binary)
        {
            if (!payload.TryReadLengthPrefixed(out bytes) || bytes.Length != size)
                return @default;
        }
        else if (!payload.TryRead(size, out bytes))
        {
            return @default;
        }
        return MemoryMarshal.Read<T>(bytes.Span);
    }

    /// <inheritdoc/>
    public override T? GetNullableValue<T>(string key)
    {
        if (!TryGetField(key, out PackedTag tag, out PackedCursor payload) || tag != PackedTag.Nullable ||
            !payload.TryReadByte(out byte present) || present == 0 ||
            !payload.TryReadLengthPrefixed(out ReadOnlyMemory<byte> bytes) || bytes.Length != Unsafe.SizeOf<T>())
            return null;

        return MemoryMarshal.Read<T>(bytes.Span);
    }

    /// <inheritdoc/>
    public override T GetEnum<T>(string key, T @default = default) => GetValue(key, @default);

    /// <inheritdoc/>
    public override string GetString(string key, string @default = "")
    {
        if (!TryGetField(key, out PackedTag tag, out PackedCursor payload) || tag != PackedTag.StringId ||
            !payload.TryReadId(_session.Strings.Count, out int id))
            return @default;

        return _session.Strings.GetString(id);
    }

    /// <inheritdoc/>
    public override void BindBinary(string key, ref ReadOnlyMemory<byte> data)
    {
        data = default;
        if (TryGetField(key, out PackedTag tag, out PackedCursor payload) && tag == PackedTag.Binary)
            payload.TryReadLengthPrefixed(out data);
    }

    /// <inheritdoc/>
    public override void BindMemory<T>(string key, Span<T> memory)
    {
        if (!TryGetRawArray<T>(key, out ReadOnlyMemory<byte> bytes))
            return;

        ReadOnlySpan<T> elements = MemoryMarshal.Cast<byte, T>(bytes.Span);
        elements[..Math.Min(elements.Length, memory.Length)].CopyTo(memory);
    }

    /// <inheritdoc/>
    public override void BindCollection<T>(string key, ICollection<T> value)
    {
        value.Clear();
        if (!TryGetRawArray<T>(key, out ReadOnlyMemory<byte> bytes))
            return;

        ReadOnlySpan<T> elements = MemoryMarshal.Cast<byte, T>(bytes.Span);
        for (int i = 0; i < elements.Length; i++)
            value.Add(elements[i]);
    }

    /// <inheritdoc/>
    public override void BindCollection(string key, ICollection<string> value)
    {
        value.Clear();
        if (!TryGetField(key, out PackedTag tag, out PackedCursor payload) || tag != PackedTag.StringArray ||
            !payload.TryReadVarUInt(out int count))
            return;

        for (int i = 0; i < count && payload.TryReadId(_session.Strings.Count, out int id); i++)
            value.Add(_session.Strings.GetString(id));
    }

    /// <inheritdoc/>
    public override void BindDictionary<TValue>(string key, IDictionary<string, TValue> value) =>
        BindMap(key, value, PackedMapKind.Unmanaged, static (_, entry) => MemoryMarshal.Read<TValue>(entry.Bytes.Span), Unsafe.SizeOf<TValue>());

    /// <inheritdoc/>
    public override void BindDictionary(string key, IDictionary<string, string> value) =>
        BindMap(key, value, PackedMapKind.StringId, static (strings, entry) => strings.GetString(entry.StringId));

    /// <inheritdoc/>
    public override void BindDictionary(string key, IDictionary<string, ReadOnlyMemory<byte>> value) =>
        BindMap(key, value, PackedMapKind.Binary, static (_, entry) => entry.Bytes);

    /// <inheritdoc/>
    public override void BindSerializable<T>(string key, T value)
    {
        if (!_table.TryGetChildTableBytes(key, _session.Strings, out ReadOnlyMemory<byte> body))
            return;

        T? item = value;
        TryLoadChild<T>(key, body, ref item);
    }

    /// <inheritdoc/>
    public override void BindSerializableOptional<T>(string key, ref T? value, Func<SerializeReadNode, T> onCreate) where T : default
    {
        if (!TryGetField(key, out PackedTag tag, out PackedCursor payload))
            return;

        if (tag == PackedTag.Null)
        {
            value = default;
            return;
        }
        if (tag == PackedTag.Table && payload.TryReadLengthPrefixed(out ReadOnlyMemory<byte> body))
            TryLoadChild(key, body, ref value, onCreate);
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
            TryLoadChild<T>(key, body, ref item);
        }
    }

    /// <inheritdoc/>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value) =>
        BindCollectionSerializable(key, value, static _ => new T());

    /// <inheritdoc/>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value, Func<SerializeReadNode, T> onCreate)
    {
        value.Clear();
        if (!_table.TryGetTableArray(key, _session.Strings, out PackedCursor elements, out int count))
            return;

        // Non-indexed collections can reorder items or discard duplicates as they are added.
        List<(T Item, ReadOnlyMemory<byte> Body)>? loaded = value is IList<T> ? null : new();
        ReadOnlyMemory<byte> origin = elements.Unread;
        for (int i = 0; i < count && elements.TryReadLengthPrefixed(out ReadOnlyMemory<byte> body); i++)
        {
            T? item = default;
            if (!TryLoadChild(key, body, ref item, onCreate, create: true))
                continue;

            try
            {
                int previousCount = loaded is null ? 0 : value.Count;
                value.Add(item!);
                if (loaded is not null && value.Count > previousCount)
                    loaded.Add((item!, body));
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                _session.MarkFailed(body);
                _session.Errors.Report($"Failed to add PackedBinary item {i} to '{key}': {ex}");
            }
        }
        if (loaded is not null)
            _session.RecordCollection(value, origin, loaded);
    }

    /// <inheritdoc/>
    public override void BindDictionarySerializable<T>(string key, IDictionary<string, T> value) =>
        BindDictionarySerializable(key, value, static _ => new T());

    /// <inheritdoc/>
    public override void BindDictionarySerializable<T>(string key, IDictionary<string, T> value, Func<SerializeReadNode, T> onCreate)
    {
        value.Clear();
        if (!_session.TryGetMap(_table, key, PackedMapKind.Table, out PackedMapCursor map))
            return;

        for (int i = 0; i < map.Count && _session.TryReadMapEntry(ref map, key, out PackedMapEntry entry); i++)
        {
            T? item = default;
            if (!TryLoadChild(key, entry.Bytes, ref item, onCreate, create: true))
                continue;

            TryAddMapEntry(key, value, entry, item!);
        }
    }

    /// <inheritdoc/>
    public override void BindReference<T>(string key, ref T? referenceable) where T : default { }

    private bool TryGetField(string key, out PackedTag tag, out PackedCursor payload) =>
        _table.TryGetField(key, _session.Strings, out tag, out payload);

    private bool TryGetRawArray<T>(string key, out ReadOnlyMemory<byte> bytes) where T : unmanaged
    {
        bytes = default;
        if (!TryGetField(key, out PackedTag tag, out PackedCursor payload))
            return false;

        int size = Unsafe.SizeOf<T>();
        if (size <= byte.MaxValue)
            return tag == PackedTag.PrimitiveArray && PackedWire.TryReadPrimitiveArray(ref payload, out int stride, out bytes) && stride == size;

        if (tag != PackedTag.BinaryArray || !payload.TryReadVarUInt(out int count))
            return false;

        for (int i = 0; i < count; i++)
        {
            if (!payload.TryReadVarUInt(out int length) || length != size)
                return false;
        }
        return count <= payload.Remaining / size && payload.TryRead(count * size, out bytes);
    }

    private bool TryLoadChild<T>(string key, ReadOnlyMemory<byte> body, ref T? value,
        Func<SerializeReadNode, T>? onCreate = null, bool create = false) where T : ISerializable
    {
        if (!_session.TryOpenChild(_table, body, key, out PackedTable child))
            return false;

        try
        {
            var node = new PackedBinaryReadNode(child, _session);
            if (onCreate is not null && (create || value is null))
                value = onCreate(node);

            value!.OnSerialize(node, SerializeMode.Load);
            ReferenceContext?.TryReadReferenceId(node, value);
            return true;
        }
        catch (Exception ex) when (_session.Errors.CanReport(ex))
        {
            _session.MarkFailed(body);
            _session.Errors.Report($"Failed to load PackedBinary object for '{key}': {ex}");
            return false;
        }
    }

    private void BindMap<T>(string key, IDictionary<string, T> value, PackedMapKind kind,
        Func<PackedStringTable.Reader, PackedMapEntry, T> readValue, int stride = 0)
    {
        value.Clear();
        if (!_session.TryGetMap(_table, key, kind, out PackedMapCursor map) || map.Stride != stride)
            return;

        for (int i = 0; i < map.Count && _session.TryReadMapEntry(ref map, key, out PackedMapEntry entry); i++)
            TryAddMapEntry(key, value, entry, readValue(_session.Strings, entry));
    }

    private void TryAddMapEntry<T>(string key, IDictionary<string, T> value, PackedMapEntry entry, T item)
    {
        string name = _session.Strings.GetString(entry.KeyId);
        try
        {
            value.Add(name, item);
        }
        catch (Exception ex) when (_session.Errors.CanReport(ex))
        {
            _session.MarkFailed(entry.Bytes);
            _session.Errors.Report($"Failed to add PackedBinary map entry '{name}' to '{key}': {ex}");
        }
    }
}
