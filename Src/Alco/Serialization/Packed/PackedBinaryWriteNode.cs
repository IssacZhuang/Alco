using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Alco;

/// <summary>
/// Writes the fields of an object to a packed binary table.
/// </summary>
internal sealed class PackedBinaryWriteNode : SerializeWriteNode
{
    private readonly WriteSession _session;
    private readonly Frame _frame;
    private int _depth;
    private int _fieldCount;

    private PackedBinaryWriteNode(WriteSession session, Frame frame)
    {
        _session = session;
        _frame = frame;
    }

    private void ResetForReuse(int depth)
    {
        _depth = depth;
        _fieldCount = 0;
    }

    /// <inheritdoc/>
    public override ReferenceContext? ReferenceContext => _session.ReferenceContext;

    /// <inheritdoc/>
    public override void EncodeSubDocument(
        IBufferWriter<byte> destination,
        ISerializable root,
        ReferenceContext? refCtx = null,
        bool elideDefaults = false)
    {
        if (_session.Owner is { } owner)
        {
            owner.EncodeTo(destination, root, refCtx, elideDefaults);
            return;
        }

        PackedBinarySerializer.EncodeTo(destination, root, refCtx, elideDefaults);
    }

    /// <inheritdoc/>
    public override void BindValue<T>(string key, ref T value, T @default = default)
    {
        if (_session.ElideDefaults && ValueBytes(in value).SequenceEqual(ValueBytes(in @default)))
            return;

        WriteScalar(key, in value);
    }

    /// <inheritdoc/>
    public override void BindEnum<T>(string key, ref T value, T @default = default) => BindValue(key, ref value, @default);

    /// <inheritdoc/>
    public override void BindString(string key, ref string value, string @default = "")
    {
        string text = value ?? string.Empty;
        if (_session.ElideDefaults && string.Equals(text, @default, StringComparison.Ordinal))
            return;

        SetString(key, text);
    }

    /// <inheritdoc/>
    public override void SetValue<T>(string key, T value) => WriteScalar(key, in value);

    /// <inheritdoc/>
    public override void SetEnum<T>(string key, T value) => WriteScalar(key, in value);

    /// <inheritdoc/>
    public override void SetNullableValue<T>(string key, T? value)
    {
        WriteFieldHeader(key, GetKeyId(key), PackedTag.Nullable);
        PackedWire.WriteByte(_frame.Bytes, value.HasValue ? (byte)1 : (byte)0);
        if (value.HasValue)
        {
            T item = value.GetValueOrDefault();
            PackedWire.WriteLengthPrefixed(_frame.Bytes, ValueBytes(in item));
        }
        _fieldCount++;
    }

    /// <inheritdoc/>
    public override void SetString(string key, string value)
    {
        int keyId = GetKeyId(key);
        int valueId = _session.Strings.GetOrAdd(value);
        WriteFieldHeader(key, keyId, PackedTag.StringId);
        PackedWire.WriteVarUInt(_frame.Bytes, valueId);
        _fieldCount++;
    }

    /// <inheritdoc/>
    public override void BindBinary(string key, ref ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> bytes = data.Span;
        WriteFieldHeader(key, GetKeyId(key), PackedTag.Binary);
        PackedWire.WriteLengthPrefixed(_frame.Bytes, bytes);
        _fieldCount++;
    }

    /// <inheritdoc/>
    public override void BindMemory<T>(string key, Span<T> memory) =>
        WriteUnmanagedArray(key, GetKeyId(key), memory.Length, Unsafe.SizeOf<T>(), MemoryMarshal.AsBytes(memory));

    /// <inheritdoc/>
    public override void BindCollection<T>(string key, ICollection<T> value)
    {
        int keyId = GetKeyId(key);
        using Frame items = _session.RentFrame();
        int count = 0;
        foreach (T item in value)
        {
            items.Bytes.Write(ValueBytes(in item));
            count++;
        }

        WriteUnmanagedArray(key, keyId, count, Unsafe.SizeOf<T>(), items.Bytes.WrittenSpan);
    }

    /// <inheritdoc/>
    public override void BindCollection(string key, ICollection<string> value)
    {
        int keyId = GetKeyId(key);
        using Frame items = _session.RentFrame();
        int count = 0;
        int index = -1;
        foreach (string item in value)
        {
            index = checked(index + 1);
            int id;
            try
            {
                id = _session.Strings.GetOrAdd(item);
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                _session.Errors.Report($"Failed to bind string collection item at index {index} for key '{key}': {ex}");
                continue;
            }

            PackedWire.WriteVarUInt(items.Bytes, id);
            count++;
        }

        WriteArrayField(key, keyId, PackedTag.StringArray, count, items.Bytes.WrittenSpan);
    }

    /// <inheritdoc/>
    public override void BindSerializable<T>(string key, T value) => BindChild(key, value, optional: false);

    /// <inheritdoc/>
    public override void BindSerializableOptional<T>(string key, ref T? value, Func<SerializeReadNode, T> onCreate) where T : default
    {
        if (value is not null)
        {
            BindChild(key, value, optional: true);
            return;
        }

        WriteFieldHeader(key, GetKeyId(key), PackedTag.Null);
        _fieldCount++;
    }

    /// <inheritdoc/>
    public override void BindArraySerializable<T>(string key, IReadOnlyList<T> value)
    {
        int keyId = GetKeyId(key);
        using Frame items = _session.RentFrame();
        int count = value.Count;
        for (int i = 0; i < count; i++)
        {
            using Frame frame = _session.RentFrame();
            PackedBinaryWriteNode child;
            try
            {
                child = SaveChild(frame, value[i]);
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                // Dropping just this item would change the position of every later item.
                _session.Errors.Report($"Failed to bind array serializable '{key}' at index {i}: {ex}");
                return;
            }

            child.WriteSizedTable(items.Bytes);
        }

        WriteArrayField(key, keyId, PackedTag.TableArray, count, items.Bytes.WrittenSpan);
    }

    /// <inheritdoc/>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value) => BindSerializableCollection(key, value);

    /// <inheritdoc/>
    public override void BindCollectionSerializable<T>(string key, ICollection<T> value, Func<SerializeReadNode, T> onCreate) =>
        BindSerializableCollection(key, value);

    /// <inheritdoc/>
    public override void BindDictionarySerializable<T>(string key, IDictionary<string, T> value) => BindSerializableMap(key, value);

    /// <inheritdoc/>
    public override void BindDictionarySerializable<T>(string key, IDictionary<string, T> value, Func<SerializeReadNode, T> onCreate) =>
        BindSerializableMap(key, value);

    /// <inheritdoc/>
    public override void BindDictionary<TValue>(string key, IDictionary<string, TValue> value)
    {
        int keyId = GetKeyId(key);
        using Frame entries = _session.RentFrame();
        int count = 0;
        foreach (var entry in value)
        {
            int entryId;
            try
            {
                entryId = GetKeyId(entry.Key);
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                ReportMapItemError(key, entry.Key, ex);
                continue;
            }

            WriteUniqueKey(entries, entry.Key, entryId);
            TValue item = entry.Value;
            entries.Bytes.Write(ValueBytes(in item));
            count++;
        }

        WriteMapField(key, keyId, PackedMapKind.Unmanaged, count, entries, Unsafe.SizeOf<TValue>());
    }

    /// <inheritdoc/>
    public override void BindDictionary(string key, IDictionary<string, string> value)
    {
        int keyId = GetKeyId(key);
        using Frame entries = _session.RentFrame();
        int count = 0;
        foreach (var entry in value)
        {
            int entryId;
            int valueId;
            try
            {
                entryId = GetKeyId(entry.Key);
                valueId = _session.Strings.GetOrAdd(entry.Value);
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                ReportMapItemError(key, entry.Key, ex);
                continue;
            }

            WriteUniqueKey(entries, entry.Key, entryId);
            PackedWire.WriteVarUInt(entries.Bytes, valueId);
            count++;
        }

        WriteMapField(key, keyId, PackedMapKind.StringId, count, entries);
    }

    /// <inheritdoc/>
    public override void BindDictionary(string key, IDictionary<string, ReadOnlyMemory<byte>> value)
    {
        int keyId = GetKeyId(key);
        using Frame entries = _session.RentFrame();
        int count = 0;
        foreach (var entry in value)
        {
            int entryId;
            ReadOnlySpan<byte> bytes;
            try
            {
                entryId = GetKeyId(entry.Key);
                bytes = entry.Value.Span;
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                ReportMapItemError(key, entry.Key, ex);
                continue;
            }

            WriteUniqueKey(entries, entry.Key, entryId);
            PackedWire.WriteLengthPrefixed(entries.Bytes, bytes);
            count++;
        }

        WriteMapField(key, keyId, PackedMapKind.Binary, count, entries);
    }

    /// <inheritdoc/>
    public override void BindReference<T>(string key, ref T? referenceable) where T : default
    {
        ReferenceContext? context = ReferenceContext;
        if (context is null || referenceable is null)
            return;

        ulong id = context.GetId(referenceable);
        context.TrackReferenced(referenceable);
        SetValue(key, id);
    }

    private void BindChild<T>(string key, T value, bool optional) where T : ISerializable
    {
        int keyId = GetKeyId(key);
        using Frame frame = _session.RentFrame();
        PackedBinaryWriteNode child;
        try
        {
            child = SaveChild(frame, value);
        }
        catch (Exception ex) when (_session.Errors.CanReport(ex))
        {
            string kind = optional ? "optional serializable" : "serializable";
            _session.Errors.Report($"Failed to bind {kind} '{key}': {ex}");
            return;
        }

        WriteFieldHeader(key, keyId, PackedTag.Table);
        child.WriteSizedTable(_frame.Bytes);
        _fieldCount++;
    }

    private void BindSerializableCollection<T>(string key, ICollection<T> value) where T : ISerializable
    {
        int keyId = GetKeyId(key);
        using Frame items = _session.RentFrame();
        int count = 0;
        int index = -1;
        foreach (T item in value)
        {
            index = checked(index + 1);
            using Frame frame = _session.RentFrame();
            PackedBinaryWriteNode child;
            try
            {
                child = SaveChild(frame, item);
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                _session.Errors.Report($"Failed to bind serializable list item at index {index} for key '{key}': {ex}");
                continue;
            }

            child.WriteSizedTable(items.Bytes);
            count++;
        }

        WriteArrayField(key, keyId, PackedTag.TableArray, count, items.Bytes.WrittenSpan);
    }

    private void BindSerializableMap<T>(string key, IDictionary<string, T> value) where T : ISerializable
    {
        int keyId = GetKeyId(key);
        using Frame entries = _session.RentFrame();
        int count = 0;
        foreach (var entry in value)
        {
            using Frame frame = _session.RentFrame();
            int entryId;
            PackedBinaryWriteNode child;
            try
            {
                entryId = GetKeyId(entry.Key);
                child = SaveChild(frame, entry.Value);
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                _session.Errors.Report($"Failed to bind serializable dictionary item key '{entry.Key}' for key '{key}': {ex}");
                continue;
            }

            WriteUniqueKey(entries, entry.Key, entryId);
            child.WriteSizedTable(entries.Bytes);
            count++;
        }

        WriteMapField(key, keyId, PackedMapKind.Table, count, entries);
    }

    private void Save<T>(T value) where T : ISerializable
    {
        if (ReferenceContext is { } refCtx && value is IReferenceable referenceable)
        {
            ulong id = refCtx.GetId(referenceable);
            SetValue(ReferenceContext.SerializeKey, id);
        }

        value.OnSerialize(this, SerializeMode.Save);
    }

    private PackedBinaryWriteNode SaveChild<T>(Frame frame, T value) where T : ISerializable
    {
        if (_depth >= PackedWire.MaxDepth)
            throw new InvalidOperationException($"Packed binary tables cannot exceed depth {PackedWire.MaxDepth}.");

        PackedBinaryWriteNode child = frame.PrepareNode(_depth + 1);
        child.Save(value);
        return child;
    }

    private static ReadOnlySpan<byte> ValueBytes<T>(in T value) where T : unmanaged =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1));

    private void WriteScalar<T>(string key, in T value) where T : unmanaged
    {
        PackedTag tag = PackedWire.GetScalarTag(Unsafe.SizeOf<T>());
        WriteFieldHeader(key, GetKeyId(key), tag);
        if (tag == PackedTag.Binary)
            PackedWire.WriteLengthPrefixed(_frame.Bytes, ValueBytes(in value));
        else
            _frame.Bytes.Write(ValueBytes(in value));
        _fieldCount++;
    }

    private void WriteUnmanagedArray(string key, int keyId, int count, int stride, ReadOnlySpan<byte> bytes)
    {
        bool isPrimitive = stride <= byte.MaxValue;
        int sizeBytes = isPrimitive ? 1 : checked(count * PackedWire.VarUIntSize(stride));
        _ = checked(PackedWire.VarUIntSize(count) + sizeBytes + bytes.Length);

        WriteFieldHeader(key, keyId, isPrimitive ? PackedTag.PrimitiveArray : PackedTag.BinaryArray);
        PackedWire.WriteVarUInt(_frame.Bytes, count);
        if (isPrimitive)
        {
            PackedWire.WriteByte(_frame.Bytes, (byte)stride);
        }
        else
        {
            // All element lengths precede the contiguous payload in the binary array.
            for (int i = 0; i < count; i++)
                PackedWire.WriteVarUInt(_frame.Bytes, stride);
        }
        _frame.Bytes.Write(bytes);
        _fieldCount++;
    }

    private void WriteArrayField(string key, int keyId, PackedTag tag, int count, ReadOnlySpan<byte> items)
    {
        WriteFieldHeader(key, keyId, tag);
        PackedWire.WriteVarUInt(_frame.Bytes, count);
        _frame.Bytes.Write(items);
        _fieldCount++;
    }

    private void WriteMapField(string key, int keyId, PackedMapKind kind, int count, Frame entries, int stride = 0)
    {
        int length = checked(PackedWire.VarUIntSize(count) + 1 + entries.Bytes.WrittenCount +
            (kind == PackedMapKind.Unmanaged ? PackedWire.VarUIntSize(stride) : 0));
        WriteFieldHeader(key, keyId, PackedTag.Map);
        PackedWire.WriteVarUInt(_frame.Bytes, length);
        PackedWire.WriteVarUInt(_frame.Bytes, count);
        PackedWire.WriteByte(_frame.Bytes, (byte)kind);
        if (kind == PackedMapKind.Unmanaged)
            PackedWire.WriteVarUInt(_frame.Bytes, stride);
        _frame.Bytes.Write(entries.Bytes.WrittenSpan);
        _fieldCount++;
    }

    private void ReportMapItemError(string key, string entryKey, Exception ex) =>
        _session.Errors.Report($"Failed to bind dictionary item key '{entryKey}' for key '{key}': {ex}");

    private int GetKeyId(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _session.Strings.GetOrAdd(key);
    }

    private static void WriteUniqueKey(Frame frame, string key, int id)
    {
        if (!frame.TryAddKey(id))
            throw new ArgumentException($"An entry with the same key has already been written: '{key}'.", nameof(key));

        PackedWire.WriteVarUInt(frame.Bytes, id);
    }

    private void WriteFieldHeader(string key, int keyId, PackedTag tag)
    {
        WriteUniqueKey(_frame, key, keyId);
        PackedWire.WriteByte(_frame.Bytes, (byte)tag);
    }

    private int GetTableByteLength() => checked(PackedWire.VarUIntSize(_fieldCount) + _frame.Bytes.WrittenCount);

    private void WriteCountedTable(IBufferWriter<byte> destination)
    {
        PackedWire.WriteVarUInt(destination, _fieldCount);
        destination.Write(_frame.Bytes.WrittenSpan);
    }

    private void WriteSizedTable(IBufferWriter<byte> destination)
    {
        PackedWire.WriteVarUInt(destination, GetTableByteLength());
        WriteCountedTable(destination);
    }

    /// <summary>
    /// Appends a packed binary document by saving the root object once.
    /// </summary>
    public static void EncodeTo(IBufferWriter<byte> destination, ISerializable root, ReferenceContext? refCtx, bool elideDefaults, PackedErrors errors)
    {
        PackedWire.RequireLittleEndian();
        var session = new WriteSession(errors) { ReferenceContext = refCtx, ElideDefaults = elideDefaults };
        EncodeTo(session, destination, root);
    }

    /// <summary>
    /// Appends a packed binary document reusing the caller-supplied <paramref name="session"/>.
    /// </summary>
    public static void EncodeTo(WriteSession session, IBufferWriter<byte> destination, ISerializable root)
    {
        PackedWire.RequireLittleEndian();
        using Frame frame = session.RentFrame();
        PackedBinaryWriteNode node = frame.PrepareNode(depth: 0);
        node.Save(root);

        int stringOffset = checked(PackedWire.HeaderSize + node.GetTableByteLength());
        PackedWire.WriteHeader(destination, session.ElideDefaults, stringOffset, session.Strings.Count);
        node.WriteCountedTable(destination);
        session.Strings.WriteTo(destination);
    }

    public sealed class WriteSession(PackedErrors errors, int initialStringCapacity = 32, PackedBinaryEncoder? owner = null)
    {
        private readonly List<Frame> _frames = new();
        private int _rentedCount;

        public PackedBinaryEncoder? Owner { get; } = owner;

        public PackedStringTable.Writer Strings { get; } = new(initialStringCapacity);

        public PackedErrors Errors { get; private set; } = errors;

        public ReferenceContext? ReferenceContext { get; set; }

        public bool ElideDefaults { get; set; } = true;

        public Frame RentFrame()
        {
            if (_rentedCount == _frames.Count)
                _frames.Add(new Frame(this));

            return _frames[_rentedCount++];
        }

        public void ReturnFrame() => _rentedCount--;

        public void Reset(ReferenceContext? refCtx, bool elideDefaults, PackedErrors errors, int maxRetainedFrameBytes = 262_144)
        {
            ReferenceContext = refCtx;
            ElideDefaults = elideDefaults;
            Errors = errors;
            Strings.Reset();
            _rentedCount = 0;

            for (int i = 0; i < _frames.Count; i++)
                _frames[i].TrimIfNeeded(maxRetainedFrameBytes);
        }
    }

    public sealed class Frame : IDisposable
    {
        private readonly WriteSession _session;
        private readonly HashSet<int> _keyIds = new();
        private readonly PackedBinaryWriteNode _node;

        public ArrayBufferWriter<byte> Bytes { get; private set; } = new();

        public Frame(WriteSession session)
        {
            _session = session;
            _node = new PackedBinaryWriteNode(session, this);
        }

        public PackedBinaryWriteNode PrepareNode(int depth)
        {
            _node.ResetForReuse(depth);
            return _node;
        }

        public bool TryAddKey(int id) => _keyIds.Add(id);

        public void TrimIfNeeded(int maxRetainedBytes)
        {
            if (Bytes.Capacity > maxRetainedBytes)
                Bytes = new ArrayBufferWriter<byte>();
        }

        public void Dispose()
        {
            Bytes.ResetWrittenCount();
            _keyIds.Clear();
            _session.ReturnFrame();
        }
    }
}
