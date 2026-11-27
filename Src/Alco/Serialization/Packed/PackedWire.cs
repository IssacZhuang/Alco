using System;
using System.Buffers;
using System.Buffers.Binary;

namespace Alco;

internal enum PackedTag : byte
{
    Null = 0,
    Primitive1 = 1,
    Primitive2 = 2,
    Primitive4 = 3,
    Primitive8 = 4,
    Primitive16 = 5,
    Nullable = 6,
    StringId = 7,
    Binary = 8,
    Table = 9,
    PrimitiveArray = 10,
    StringArray = 11,
    BinaryArray = 12,
    TableArray = 13,
    Map = 14,
}

internal enum PackedMapKind : byte
{
    Unmanaged = 0,
    StringId = 1,
    Binary = 2,
    Table = 3,
}

internal static class PackedWire
{
    public const int HeaderSize = 16;
    public const int MaxDepth = 128;
    private const ushort Version = 1;
    private const ushort ElideDefaults = 1;

    public static void RequireLittleEndian()
    {
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("PackedBinary unmanaged values require a little-endian runtime.");
    }

    public static void WriteHeader(IBufferWriter<byte> output, bool elideDefaults, int stringTableOffset, int stringCount)
    {
        Span<byte> header = output.GetSpan(HeaderSize)[..HeaderSize];
        "APKB"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], elideDefaults ? ElideDefaults : (ushort)0);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], stringTableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], stringCount);
        output.Advance(HeaderSize);
    }

    public static bool TryReadHeader(ReadOnlySpan<byte> data, out int stringTableOffset, out int stringCount)
    {
        stringTableOffset = 0;
        stringCount = 0;
        if (data.Length <= HeaderSize || !data[..4].SequenceEqual("APKB"u8) ||
            BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != Version ||
            (BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & ~ElideDefaults) != 0)
            return false;

        stringTableOffset = BinaryPrimitives.ReadInt32LittleEndian(data[8..]);
        stringCount = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        return stringTableOffset > HeaderSize && stringTableOffset <= data.Length &&
            stringCount >= 0 && stringCount <= data.Length - stringTableOffset;
    }

    public static int VarUIntSize(int value)
    {
        int size = 1;
        while ((value >>= 7) > 0)
            size++;
        return size;
    }

    public static void WriteVarUInt(IBufferWriter<byte> output, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Span<byte> bytes = output.GetSpan(5);
        int size = 0;
        while (value >= 0x80)
        {
            bytes[size++] = (byte)(value | 0x80);
            value >>= 7;
        }
        bytes[size++] = (byte)value;
        output.Advance(size);
    }

    public static void WriteByte(IBufferWriter<byte> output, byte value)
    {
        output.GetSpan(1)[0] = value;
        output.Advance(1);
    }

    public static void WriteLengthPrefixed(IBufferWriter<byte> output, ReadOnlySpan<byte> value)
    {
        WriteVarUInt(output, value.Length);
        output.Write(value);
    }

    public static PackedTag GetScalarTag(int size) => size switch
    {
        1 => PackedTag.Primitive1,
        2 => PackedTag.Primitive2,
        4 => PackedTag.Primitive4,
        8 => PackedTag.Primitive8,
        16 => PackedTag.Primitive16,
        _ => PackedTag.Binary,
    };

    public static bool TryReadPrimitiveArray(ref PackedCursor cursor, out int stride, out ReadOnlyMemory<byte> values)
    {
        stride = 0;
        values = default;
        if (!cursor.TryReadVarUInt(out int count) || !cursor.TryReadByte(out byte size) ||
            size == 0 || count > cursor.Remaining / size)
            return false;

        stride = size;
        return cursor.TryRead(count * size, out values);
    }

    public static bool TrySkipPayload(ref PackedCursor cursor, PackedTag tag, int stringCount)
    {
        switch (tag)
        {
            case PackedTag.Null: return true;
            case PackedTag.Primitive1: return cursor.TrySkip(1);
            case PackedTag.Primitive2: return cursor.TrySkip(2);
            case PackedTag.Primitive4: return cursor.TrySkip(4);
            case PackedTag.Primitive8: return cursor.TrySkip(8);
            case PackedTag.Primitive16: return cursor.TrySkip(16);
            case PackedTag.Nullable:
                return cursor.TryReadByte(out byte present) && (present == 0 ||
                    (present == 1 && cursor.TryReadVarUInt(out int size) && size > 0 && cursor.TrySkip(size)));
            case PackedTag.StringId:
                return cursor.TryReadId(stringCount, out _);
            case PackedTag.Binary:
            case PackedTag.Table:
            case PackedTag.Map:
                return cursor.TrySkipLengthPrefixed();
            case PackedTag.PrimitiveArray:
                return TryReadPrimitiveArray(ref cursor, out _, out _);
            case PackedTag.StringArray:
                if (!TryReadVariableCount(ref cursor, out int strings))
                    return false;
                for (int i = 0; i < strings; i++)
                {
                    if (!cursor.TryReadId(stringCount, out _))
                        return false;
                }
                return true;
            case PackedTag.BinaryArray:
                if (!TryReadVariableCount(ref cursor, out int binaries))
                    return false;
                int total = 0;
                for (int i = 0; i < binaries; i++)
                {
                    if (!cursor.TryReadVarUInt(out int length) || length > cursor.Remaining - total)
                        return false;
                    total += length;
                }
                return cursor.TrySkip(total);
            case PackedTag.TableArray:
                if (!TryReadVariableCount(ref cursor, out int tables))
                    return false;
                for (int i = 0; i < tables; i++)
                {
                    if (!cursor.TrySkipLengthPrefixed())
                        return false;
                }
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadVariableCount(ref PackedCursor cursor, out int count) =>
        cursor.TryReadVarUInt(out count) && count <= cursor.Remaining;
}

internal struct PackedCursor(ReadOnlyMemory<byte> data)
{
    public int Position { get; private set; }
    public readonly int Remaining => data.Length - Position;
    public readonly ReadOnlyMemory<byte> Unread => data[Position..];

    public bool TryReadByte(out byte value)
    {
        value = 0;
        if (Remaining == 0)
            return false;

        value = data.Span[Position++];
        return true;
    }

    public bool TryReadVarUInt(out int value)
    {
        value = 0;
        ReadOnlySpan<byte> bytes = data.Span;
        uint bits = 0;
        int end = Position;
        for (int shift = 0; shift <= 28 && end < bytes.Length; shift += 7)
        {
            uint next = bytes[end++];
            if (shift == 28 && next > 7)
                return false;

            bits |= (next & 0x7fu) << shift;
            if (next >= 0x80)
                continue;
            if (shift > 0 && next == 0)
                return false;

            Position = end;
            value = (int)bits;
            return true;
        }
        return false;
    }

    public bool TryReadId(int stringCount, out int id) => TryReadVarUInt(out id) && id < stringCount;

    public bool TryRead(int length, out ReadOnlyMemory<byte> bytes)
    {
        bytes = default;
        if ((uint)length > (uint)Remaining)
            return false;

        bytes = data.Slice(Position, length);
        Position += length;
        return true;
    }

    public bool TryReadLengthPrefixed(out ReadOnlyMemory<byte> bytes)
    {
        bytes = default;
        return TryReadVarUInt(out int length) && TryRead(length, out bytes);
    }

    public bool TrySkipLengthPrefixed() => TryReadVarUInt(out int length) && TrySkip(length);

    public bool TrySkip(int length)
    {
        if ((uint)length > (uint)Remaining)
            return false;
        Position += length;
        return true;
    }
}
