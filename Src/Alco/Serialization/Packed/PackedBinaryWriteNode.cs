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
        RewindableByteBuffer body = _session.Body;
        PackedWire.WriteByte(body, value.HasValue ? (byte)1 : (byte)0);
        if (value.HasValue)
        {
            T item = value.GetValueOrDefault();
            PackedWire.WriteLengthPrefixed(body, ValueBytes(in item));
        }
        _fieldCount++;
    }

    /// <inheritdoc/>
    public override void SetString(string key, string value)
    {
        int keyId = GetKeyId(key);
        int valueId = _session.Strings.GetOrAdd(value);
        WriteFieldHeader(key, keyId, PackedTag.StringId);
        PackedWire.WriteVarUInt(_session.Body, valueId);
        _fieldCount++;
    }

    /// <inheritdoc/>
    public override ScratchBufferScope RentScratchBuffer() => _session.RentScratchScope();

    /// <inheritdoc/>
    public override void SetBinary(string key, ReadOnlySpan<byte> data)
    {
        WriteFieldHeader(key, GetKeyId(key), PackedTag.Binary);
        PackedWire.WriteLengthPrefixed(_session.Body, data);
        _fieldCount++;
    }

    /// <inheritdoc/>
    public override bool TryReserveBinary(string key, int byteLength, out Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(byteLength);
        WriteFieldHeader(key, GetKeyId(key), PackedTag.Binary);
        PackedWire.WriteVarUInt(_session.Body, byteLength);
        destination = _session.Body.Reserve(byteLength);
        _fieldCount++;
        return true;
    }

    /// <inheritdoc/>
    public override void BindBinary(string key, ref ReadOnlyMemory<byte> data)
    {
        SetBinary(key, data.Span);
    }

    /// <inheritdoc/>
    public override void BindMemory<T>(string key, Span<T> memory) =>
        WriteUnmanagedArray(key, GetKeyId(key), memory.Length, Unsafe.SizeOf<T>(), MemoryMarshal.AsBytes(memory));

    /// <inheritdoc/>
    public override void BindCollection<T>(string key, ICollection<T> value)
    {
        int keyId = GetKeyId(key);
        int stride = Unsafe.SizeOf<T>();
        if (stride > byte.MaxValue)
        {
            using ScratchBufferScope items = _session.RentScratchScope();
            int largeCount = 0;
            foreach (T item in value)
            {
                items.Writer.Write(ValueBytes(in item));
                largeCount++;
            }

            WriteUnmanagedArray(key, keyId, largeCount, stride, items.WrittenSpan);
            return;
        }

        WriteFieldHeader(key, keyId, PackedTag.PrimitiveArray);
        RewindableByteBuffer body = _session.Body;
        int headerPos = body.WrittenCount;
        body.Reserve(2);
        int contentStart = headerPos + 2;

        int count = 0;
        foreach (T item in value)
        {
            body.Write(ValueBytes(in item));
            count++;
        }

        int countLen = PackedWire.VarUIntSize(count);
        if (countLen == 1)
        {
            Span<byte> header = body.RawSpan.Slice(headerPos, 2);
            header[0] = (byte)count;
            header[1] = (byte)stride;
        }
        else
        {
            body.ShiftRight(contentStart, body.WrittenCount - contentStart, countLen - 1);
            Span<byte> header = body.RawSpan.Slice(headerPos, countLen + 1);
            int written = WriteVarUIntToSpan(header, count);
            header[written] = (byte)stride;
        }

        _fieldCount++;
    }

    /// <inheritdoc/>
    public override void BindCollection(string key, ICollection<string> value)
    {
        int keyId = GetKeyId(key);
        WriteFieldHeader(key, keyId, PackedTag.StringArray);
        RewindableByteBuffer body = _session.Body;
        int headerPos = body.WrittenCount;
        body.Reserve(1);
        int contentStart = headerPos + 1;

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

            PackedWire.WriteVarUInt(body, id);
            count++;
        }

        int countLen = PackedWire.VarUIntSize(count);
        if (countLen == 1)
        {
            body.RawSpan[headerPos] = (byte)count;
        }
        else
        {
            body.ShiftRight(contentStart, body.WrittenCount - contentStart, countLen - 1);
            WriteVarUIntToSpan(body.RawSpan.Slice(headerPos, countLen), count);
        }

        _fieldCount++;
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
        RewindableByteBuffer body = _session.Body;
        int rollbackPos = body.WrittenCount;
        WriteFieldHeader(key, keyId, PackedTag.TableArray);

        int count = value.Count;
        PackedWire.WriteVarUInt(body, count);
        for (int i = 0; i < count; i++)
        {
            try
            {
                WriteSizedChild(value[i]);
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                // Dropping just this item would change the position of every later item.
                body.Rewind(rollbackPos);
                _frame.RemoveKey(keyId);
                _session.Errors.Report($"Failed to bind array serializable '{key}' at index {i}: {ex}");
                return;
            }
        }

        _fieldCount++;
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
        using Frame entriesFrame = _session.RentFrame();
        int stride = Unsafe.SizeOf<TValue>();
        int strideLen = PackedWire.VarUIntSize(stride);
        RewindableByteBuffer body = _session.Body;

        BeginMapField(key, keyId, strideLen, out int headerPos, out int reservedHeaderLen, out int contentStart);
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

            WriteUniqueKey(entriesFrame, entry.Key, entryId);
            TValue item = entry.Value;
            body.Write(ValueBytes(in item));
            count++;
        }

        EndMapField(PackedMapKind.Unmanaged, count, headerPos, reservedHeaderLen, contentStart, stride, strideLen);
    }

    /// <inheritdoc/>
    public override void BindDictionary(string key, IDictionary<string, string> value)
    {
        int keyId = GetKeyId(key);
        using Frame entriesFrame = _session.RentFrame();
        RewindableByteBuffer body = _session.Body;

        BeginMapField(key, keyId, strideLen: 0, out int headerPos, out int reservedHeaderLen, out int contentStart);
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

            WriteUniqueKey(entriesFrame, entry.Key, entryId);
            PackedWire.WriteVarUInt(body, valueId);
            count++;
        }

        EndMapField(PackedMapKind.StringId, count, headerPos, reservedHeaderLen, contentStart);
    }

    /// <inheritdoc/>
    public override void BindDictionary(string key, IDictionary<string, ReadOnlyMemory<byte>> value)
    {
        int keyId = GetKeyId(key);
        using Frame entriesFrame = _session.RentFrame();
        RewindableByteBuffer body = _session.Body;

        BeginMapField(key, keyId, strideLen: 0, out int headerPos, out int reservedHeaderLen, out int contentStart);
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

            WriteUniqueKey(entriesFrame, entry.Key, entryId);
            PackedWire.WriteLengthPrefixed(body, bytes);
            count++;
        }

        EndMapField(PackedMapKind.Binary, count, headerPos, reservedHeaderLen, contentStart);
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
        RewindableByteBuffer body = _session.Body;
        int rollbackPos = body.WrittenCount;
        WriteFieldHeader(key, keyId, PackedTag.Table);
        try
        {
            WriteSizedChild(value);
        }
        catch (Exception ex) when (_session.Errors.CanReport(ex))
        {
            body.Rewind(rollbackPos);
            _frame.RemoveKey(keyId);
            string kind = optional ? "optional serializable" : "serializable";
            _session.Errors.Report($"Failed to bind {kind} '{key}': {ex}");
            return;
        }

        _fieldCount++;
    }

    private void BindSerializableCollection<T>(string key, ICollection<T> value) where T : ISerializable
    {
        int keyId = GetKeyId(key);
        WriteFieldHeader(key, keyId, PackedTag.TableArray);
        RewindableByteBuffer body = _session.Body;
        int headerPos = body.WrittenCount;
        body.Reserve(1);
        int contentStart = headerPos + 1;

        int count = 0;
        int index = -1;
        foreach (T item in value)
        {
            index = checked(index + 1);
            int itemStart = body.WrittenCount;
            try
            {
                WriteSizedChild(item);
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                body.Rewind(itemStart);
                _session.Errors.Report($"Failed to bind serializable list item at index {index} for key '{key}': {ex}");
                continue;
            }

            count++;
        }

        int countLen = PackedWire.VarUIntSize(count);
        if (countLen == 1)
        {
            body.RawSpan[headerPos] = (byte)count;
        }
        else
        {
            body.ShiftRight(contentStart, body.WrittenCount - contentStart, countLen - 1);
            WriteVarUIntToSpan(body.RawSpan.Slice(headerPos, countLen), count);
        }

        _fieldCount++;
    }

    private void BindSerializableMap<T>(string key, IDictionary<string, T> value) where T : ISerializable
    {
        int keyId = GetKeyId(key);
        using Frame entriesFrame = _session.RentFrame();
        RewindableByteBuffer body = _session.Body;

        BeginMapField(key, keyId, strideLen: 0, out int headerPos, out int reservedHeaderLen, out int contentStart);
        int count = 0;
        foreach (var entry in value)
        {
            int entryStart = body.WrittenCount;
            int entryId;
            try
            {
                entryId = GetKeyId(entry.Key);
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                _session.Errors.Report($"Failed to bind serializable dictionary item key '{entry.Key}' for key '{key}': {ex}");
                continue;
            }

            WriteUniqueKey(entriesFrame, entry.Key, entryId);

            try
            {
                WriteSizedChild(entry.Value);
            }
            catch (Exception ex) when (_session.Errors.CanReport(ex))
            {
                body.Rewind(entryStart);
                entriesFrame.RemoveKey(entryId);
                _session.Errors.Report($"Failed to bind serializable dictionary item key '{entry.Key}' for key '{key}': {ex}");
                continue;
            }

            count++;
        }

        EndMapField(PackedMapKind.Table, count, headerPos, reservedHeaderLen, contentStart);
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

    private void WriteSizedChild<T>(T value) where T : ISerializable
    {
        if (_depth >= PackedWire.MaxDepth)
            throw new InvalidOperationException($"Packed binary tables cannot exceed depth {PackedWire.MaxDepth}.");

        RewindableByteBuffer body = _session.Body;
        int headerPos = body.WrittenCount;
        body.Reserve(2);
        int contentStart = headerPos + 2;

        int childFieldCount;
        using (Frame frame = _session.RentFrame())
        {
            PackedBinaryWriteNode child = frame.PrepareNode(_depth + 1);
            child.Save(value);
            childFieldCount = child._fieldCount;
        }

        int contentLen = body.WrittenCount - contentStart;
        int fieldCountLen = PackedWire.VarUIntSize(childFieldCount);
        int tableByteLen = checked(fieldCountLen + contentLen);
        int sizePrefixLen = PackedWire.VarUIntSize(tableByteLen);
        int actualHeaderLen = checked(sizePrefixLen + fieldCountLen);

        if (actualHeaderLen == 2)
        {
            Span<byte> header = body.RawSpan.Slice(headerPos, 2);
            header[0] = (byte)tableByteLen;
            header[1] = (byte)childFieldCount;
            return;
        }

        body.ShiftRight(contentStart, contentLen, actualHeaderLen - 2);
        Span<byte> expanded = body.RawSpan.Slice(headerPos, actualHeaderLen);
        int offset = WriteVarUIntToSpan(expanded, tableByteLen);
        WriteVarUIntToSpan(expanded[offset..], childFieldCount);
    }

    private void BeginMapField(
        string key,
        int keyId,
        int strideLen,
        out int headerPos,
        out int reservedHeaderLen,
        out int contentStart)
    {
        WriteFieldHeader(key, keyId, PackedTag.Map);
        RewindableByteBuffer body = _session.Body;
        headerPos = body.WrittenCount;
        reservedHeaderLen = 3 + strideLen;
        body.Reserve(reservedHeaderLen);
        contentStart = headerPos + reservedHeaderLen;
    }

    private void EndMapField(
        PackedMapKind kind,
        int count,
        int headerPos,
        int reservedHeaderLen,
        int contentStart,
        int stride = 0,
        int strideLen = 0)
    {
        RewindableByteBuffer body = _session.Body;
        int entriesLen = body.WrittenCount - contentStart;
        int countLen = PackedWire.VarUIntSize(count);
        int mapBodyLen = checked(countLen + 1 + strideLen + entriesLen);
        int mapLenPrefixLen = PackedWire.VarUIntSize(mapBodyLen);
        int actualHeaderLen = checked(mapLenPrefixLen + countLen + 1 + strideLen);

        if (actualHeaderLen > reservedHeaderLen)
            body.ShiftRight(contentStart, entriesLen, actualHeaderLen - reservedHeaderLen);

        Span<byte> header = body.RawSpan.Slice(headerPos, actualHeaderLen);
        int offset = WriteVarUIntToSpan(header, mapBodyLen);
        offset += WriteVarUIntToSpan(header[offset..], count);
        header[offset++] = (byte)kind;
        if (kind == PackedMapKind.Unmanaged)
            WriteVarUIntToSpan(header[offset..], stride);

        _fieldCount++;
    }

    private static int WriteVarUIntToSpan(Span<byte> dest, int value)
    {
        int size = 0;
        while (value >= 0x80)
        {
            dest[size++] = (byte)(value | 0x80);
            value >>= 7;
        }
        dest[size++] = (byte)value;
        return size;
    }

    private static ReadOnlySpan<byte> ValueBytes<T>(in T value) where T : unmanaged =>
        MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1));

    private void WriteScalar<T>(string key, in T value) where T : unmanaged
    {
        PackedTag tag = PackedWire.GetScalarTag(Unsafe.SizeOf<T>());
        WriteFieldHeader(key, GetKeyId(key), tag);
        RewindableByteBuffer body = _session.Body;
        if (tag == PackedTag.Binary)
            PackedWire.WriteLengthPrefixed(body, ValueBytes(in value));
        else
            body.Write(ValueBytes(in value));
        _fieldCount++;
    }

    private void WriteUnmanagedArray(string key, int keyId, int count, int stride, ReadOnlySpan<byte> bytes)
    {
        bool isPrimitive = stride <= byte.MaxValue;
        int sizeBytes = isPrimitive ? 1 : checked(count * PackedWire.VarUIntSize(stride));
        _ = checked(PackedWire.VarUIntSize(count) + sizeBytes + bytes.Length);

        WriteFieldHeader(key, keyId, isPrimitive ? PackedTag.PrimitiveArray : PackedTag.BinaryArray);
        RewindableByteBuffer body = _session.Body;
        PackedWire.WriteVarUInt(body, count);
        if (isPrimitive)
        {
            PackedWire.WriteByte(body, (byte)stride);
        }
        else
        {
            // All element lengths precede the contiguous payload in the binary array.
            for (int i = 0; i < count; i++)
                PackedWire.WriteVarUInt(body, stride);
        }
        body.Write(bytes);
        _fieldCount++;
    }

    private void ReportMapItemError(string key, string entryKey, Exception ex) =>
        _session.Errors.Report($"Failed to bind dictionary item key '{entryKey}' for key '{key}': {ex}");

    private int GetKeyId(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _session.Strings.GetOrAdd(key);
    }

    private void WriteUniqueKey(Frame frame, string key, int id)
    {
        if (!frame.TryAddKey(id))
            throw new ArgumentException($"An entry with the same key has already been written: '{key}'.", nameof(key));

        PackedWire.WriteVarUInt(_session.Body, id);
    }

    private void WriteFieldHeader(string key, int keyId, PackedTag tag)
    {
        WriteUniqueKey(_frame, key, keyId);
        PackedWire.WriteByte(_session.Body, (byte)tag);
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
        session.Body.Rewind(0);
        int fieldCount;
        using (Frame frame = session.RentFrame())
        {
            PackedBinaryWriteNode node = frame.PrepareNode(depth: 0);
            node.Save(root);
            fieldCount = node._fieldCount;
        }

        int tableByteLength = checked(PackedWire.VarUIntSize(fieldCount) + session.Body.WrittenCount);
        int stringOffset = checked(PackedWire.HeaderSize + tableByteLength);
        PackedWire.WriteHeader(destination, session.ElideDefaults, stringOffset, session.Strings.Count);
        PackedWire.WriteVarUInt(destination, fieldCount);
        destination.Write(session.Body.WrittenSpan);
        session.Strings.WriteTo(destination);
    }

    internal sealed class RewindableByteBuffer(int initialCapacity = 16384) : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[Math.Max(256, initialCapacity)];
        private int _written;

        public int WrittenCount => _written;

        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

        public Span<byte> RawSpan => _buffer.AsSpan();

        public void Rewind(int position) => _written = position;

        public void Reset(int maxRetainedBytes)
        {
            _written = 0;
            if (_buffer.Length > maxRetainedBytes)
                _buffer = new byte[16384];
        }

        public Span<byte> Reserve(int count)
        {
            EnsureCapacity(count);
            Span<byte> span = _buffer.AsSpan(_written, count);
            _written += count;
            return span;
        }

        public void ShiftRight(int contentStart, int contentLength, int shift)
        {
            if (shift <= 0)
                return;

            EnsureCapacity(shift);
            if (contentLength > 0)
                _buffer.AsSpan(contentStart, contentLength).CopyTo(_buffer.AsSpan(contentStart + shift, contentLength));

            _written += shift;
        }

        public void Write(ReadOnlySpan<byte> source)
        {
            if (source.IsEmpty)
                return;

            EnsureCapacity(source.Length);
            source.CopyTo(_buffer.AsSpan(_written));
            _written += source.Length;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(Math.Max(1, sizeHint));
            return _buffer.AsSpan(_written);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(Math.Max(1, sizeHint));
            return _buffer.AsMemory(_written);
        }

        public void Advance(int count)
        {
            if ((uint)count > (uint)(_buffer.Length - _written))
                throw new ArgumentOutOfRangeException(nameof(count));

            _written += count;
        }

        private void EnsureCapacity(int required)
        {
            int needed = _written + required;
            if (needed <= _buffer.Length)
                return;

            int newSize = _buffer.Length;
            while (newSize < needed)
                newSize = newSize <= Array.MaxLength / 2 ? newSize * 2 : Array.MaxLength;

            Array.Resize(ref _buffer, newSize);
        }
    }

    public sealed class WriteSession(PackedErrors errors, int initialStringCapacity = 32, PackedBinaryEncoder? owner = null)
    {
        private readonly List<Frame> _frames = new();
        private readonly List<ScratchSlot> _scratchSlots = new();
        private int _rentedCount;
        private int _rentedScratchCount;

        public PackedBinaryEncoder? Owner { get; } = owner;

        public RewindableByteBuffer Body { get; } = new(16384);

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

        public ScratchBufferScope RentScratchScope()
        {
            if (_rentedScratchCount == _scratchSlots.Count)
                _scratchSlots.Add(new ScratchSlot(this));

            ScratchSlot slot = _scratchSlots[_rentedScratchCount++];
            return new ScratchBufferScope(slot.Writer, slot);
        }

        private void ReturnScratchSlot() => _rentedScratchCount--;

        public void Reset(ReferenceContext? refCtx, bool elideDefaults, PackedErrors errors, int maxRetainedFrameBytes = 262_144)
        {
            ReferenceContext = refCtx;
            ElideDefaults = elideDefaults;
            Errors = errors;
            Body.Reset(maxRetainedFrameBytes);
            Strings.Reset();
            _rentedCount = 0;
            _rentedScratchCount = 0;

            for (int i = 0; i < _scratchSlots.Count; i++)
                _scratchSlots[i].TrimIfNeeded(maxRetainedFrameBytes);
        }

        private sealed class ScratchSlot(WriteSession session) : IDisposable
        {
            public ArrayBufferWriter<byte> Writer { get; private set; } = new(1024);

            public void TrimIfNeeded(int maxRetainedBytes)
            {
                if (Writer.Capacity > maxRetainedBytes)
                    Writer = new ArrayBufferWriter<byte>(1024);
                else
                    Writer.ResetWrittenCount();
            }

            public void Dispose()
            {
                Writer.ResetWrittenCount();
                session.ReturnScratchSlot();
            }
        }
    }

    public sealed class Frame : IDisposable
    {
        private readonly WriteSession _session;
        private readonly PackedBinaryWriteNode _node;
        private ulong _smallKeyBits;
        private HashSet<int>? _overflowKeyIds;
        private bool _hasOverflowKeys;

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

        public bool TryAddKey(int id)
        {
            if ((uint)id < 64u)
            {
                ulong bit = 1UL << id;
                if ((_smallKeyBits & bit) != 0UL)
                    return false;

                _smallKeyBits |= bit;
                return true;
            }

            _overflowKeyIds ??= new HashSet<int>();
            if (!_overflowKeyIds.Add(id))
                return false;

            _hasOverflowKeys = true;
            return true;
        }

        public void RemoveKey(int id)
        {
            if ((uint)id < 64u)
            {
                _smallKeyBits &= ~(1UL << id);
                return;
            }

            _overflowKeyIds?.Remove(id);
        }

        public void Dispose()
        {
            _smallKeyBits = 0UL;
            if (_hasOverflowKeys)
            {
                _overflowKeyIds!.Clear();
                _hasOverflowKeys = false;
            }
            _session.ReturnFrame();
        }
    }
}
