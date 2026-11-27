using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;

namespace Alco.IO;

[Flags]
internal enum SegmentFlags : ushort
{
    None = 0,
    Spill = 1 << 0,
    Compacted = 1 << 1
}

[Flags]
internal enum SnapshotFlags : byte
{
    None = 0,
    HasThumbnail = 1 << 0
}

internal readonly record struct SnapshotHeaderData(
    ushort FormatVersion,
    ChunkCodecId CoreCodec,
    SnapshotFlags Flags,
    Guid WorldGuid,
    uint MetaOffset,
    uint MetaLength,
    uint ThumbOffset,
    uint ThumbLength,
    uint CoreOffset,
    uint CoreCompressedLength,
    uint CoreUncompressedLength,
    uint IndexOffset,
    uint ChunkIndexCount,
    uint HeaderCrc32);

internal static class StreamingSaveFormat
{
    public const int SegmentHeaderSize = 64;
    public const ushort SegmentFormatVersion = 1;

    public const int ChunkRecordHeaderSize = 32;
    public const int ChunkRecordTrailerSize = 8;
    public const int RecordAlignment = 16;
    public const byte DefaultChunkSchemaVersion = 1;

    public const int SnapshotHeaderSize = 64;
    public const ushort SnapshotFormatVersion = 1;
    public const int ChunkIndexEntrySize = 32;
    public const int WholeFileTrailerSize = 8;

    public const int AsavHeaderSize = 32;
    public const ushort AsavFormatVersion = 1;

    public const int ChunkPayloadHeaderSize = 16;
    public const ushort ChunkPayloadVersion = 1;
    public const int ChunkSectionDescSize = 8;

    public const long DefaultMaxSegmentBytes = 32L * 1024 * 1024;
    public const int DefaultMaxUncompressedChunkBytes = 64 * 1024 * 1024;
    public const int DefaultMaxUncompressedCoreBytes = 256 * 1024 * 1024;

    public static ReadOnlySpan<byte> SegmentMagicBytes => "ACSG"u8;
    public static ReadOnlySpan<byte> ChunkRecordMagicBytes => "ACRC"u8;
    public static ReadOnlySpan<byte> SnapshotMagicBytes => "ASNP"u8;
    public static ReadOnlySpan<byte> AsavMagicBytes => "ASAV"u8;
    public static ReadOnlySpan<byte> ChunkPayloadMagicBytes => "ACHK"u8;

    public static long Align16(long value) => (value + (RecordAlignment - 1L)) & ~(RecordAlignment - 1L);

    public static int Align16(int value) => (value + (RecordAlignment - 1)) & ~(RecordAlignment - 1);

    public static long AlignedChunkRecordLength(uint compressedSize) =>
        Align16((long)ChunkRecordHeaderSize + compressedSize + ChunkRecordTrailerSize);

    public static void WriteGuidRfc4122(Span<byte> destination, Guid value)
    {
        if (!value.TryWriteBytes(destination, bigEndian: true, out int written) || written != 16)
            throw new InvalidOperationException("Failed to write 16-byte RFC 4122 GUID.");
    }

    public static Guid ReadGuidRfc4122(ReadOnlySpan<byte> source) =>
        new(source[..16], bigEndian: true);

    public static void WriteSegmentHeader(
        Span<byte> destination,
        SegmentFlags flags,
        uint segmentId,
        Guid worldGuid,
        long createdUtcTicks)
    {
        destination[..SegmentHeaderSize].Clear();
        SegmentMagicBytes.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), SegmentFormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), (ushort)flags);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), segmentId);
        // Reserved0 [12..16] remains 0.
        WriteGuidRfc4122(destination.Slice(16, 16), worldGuid);
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(32, 8), createdUtcTicks);
        // Reserved1 [40..64] remains 0.
    }

    public static bool TryReadAndValidateSegmentHeader(
        IRandomAccessHandle handle,
        Guid expectedWorldGuid,
        uint expectedSegmentId,
        bool expectedSpill,
        out SegmentFlags flags,
        [NotNullWhen(false)] out string? failureReason)
    {
        flags = SegmentFlags.None;
        if (handle.Length < SegmentHeaderSize)
        {
            failureReason = "Segment file is shorter than 64-byte header.";
            return false;
        }

        Span<byte> header = stackalloc byte[SegmentHeaderSize];
        if (handle.ReadAt(0, header) != SegmentHeaderSize)
        {
            failureReason = "Failed to read 64-byte segment header.";
            return false;
        }

        if (!header[..4].SequenceEqual(SegmentMagicBytes))
        {
            failureReason = "Invalid segment magic header.";
            return false;
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2));
        if (version != SegmentFormatVersion)
        {
            failureReason = $"Unsupported segment version {version}.";
            return false;
        }

        ushort rawFlags = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2));
        if ((rawFlags & ~(ushort)(SegmentFlags.Spill | SegmentFlags.Compacted)) != 0)
        {
            failureReason = $"Unknown segment flags 0x{rawFlags:X4}.";
            return false;
        }

        flags = (SegmentFlags)rawFlags;
        bool isSpill = (flags & SegmentFlags.Spill) != 0;
        if (isSpill != expectedSpill)
        {
            failureReason = expectedSpill ? "Expected spill segment flag." : "Committed segment unexpectedly marked as spill.";
            return false;
        }

        uint segmentId = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));
        if (segmentId != expectedSegmentId)
        {
            failureReason = $"Segment ID mismatch: expected {expectedSegmentId}, got {segmentId}.";
            return false;
        }

        uint reserved0 = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, 4));
        if (reserved0 != 0)
        {
            failureReason = "Corrupt segment header reserved field.";
            return false;
        }

        Guid worldGuid = ReadGuidRfc4122(header.Slice(16, 16));
        if (worldGuid != expectedWorldGuid)
        {
            failureReason = $"Segment world GUID mismatch: expected {expectedWorldGuid:N}, got {worldGuid:N}.";
            return false;
        }

        failureReason = null;
        return true;
    }

    public static int WriteChunkRecord(
        Span<byte> destination,
        in ChunkIndexEntry entry,
        ReadOnlySpan<byte> compressedPayload)
    {
        if (compressedPayload.Length != entry.CompressedSize)
            throw new ArgumentException("Compressed payload length does not match index entry.", nameof(compressedPayload));

        int crcInputLength = ChunkRecordHeaderSize + compressedPayload.Length;
        int totalRecordLength = crcInputLength + ChunkRecordTrailerSize;
        int alignedLength = Align16(totalRecordLength);

        destination[..ChunkRecordHeaderSize].Clear();
        ChunkRecordMagicBytes.CopyTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), entry.MapId);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8, 4), entry.ChunkX);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12, 4), entry.ChunkY);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), entry.CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), entry.UncompressedSize);
        destination[24] = (byte)entry.Codec;
        destination[25] = entry.SchemaVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(26, 2), entry.Flags);
        // Reserved [28..32] remains 0.

        if (!compressedPayload.IsEmpty)
            compressedPayload.CopyTo(destination.Slice(ChunkRecordHeaderSize, compressedPayload.Length));

        ulong crc = XxHash64.HashToUInt64(destination[..crcInputLength]);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(crcInputLength, ChunkRecordTrailerSize), crc);

        if (alignedLength > totalRecordLength)
            destination[totalRecordLength..alignedLength].Clear();

        return alignedLength;
    }

    public static bool TryReadVerifiedCompressedRecord(
        IRandomAccessHandle handle,
        long fileOffset,
        in ChunkIndexEntry expected,
        IBufferWriter<byte> compressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        if (!TryReadVerifiedRecordBuffer(handle, fileOffset, expected, out byte[]? rentedBuffer, out int compressedLength, out failureReason))
            return false;

        try
        {
            if (compressedLength > 0)
            {
                Span<byte> dst = compressedDestination.GetSpan(compressedLength)[..compressedLength];
                rentedBuffer.AsSpan(ChunkRecordHeaderSize, compressedLength).CopyTo(dst);
                compressedDestination.Advance(compressedLength);
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    public static bool TryReadAndDecompressChunkRecord(
        IRandomAccessHandle handle,
        long fileOffset,
        in ChunkIndexEntry expected,
        ChunkCodecSet codecs,
        int maxUncompressedBytes,
        IBufferWriter<byte> uncompressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        if (!codecs.TryGetCodec(expected.Codec, out IChunkCodec? codec))
        {
            failureReason = $"Unknown chunk codec ID {(byte)expected.Codec}.";
            return false;
        }

        if (expected.UncompressedSize > (uint)maxUncompressedBytes)
        {
            failureReason = $"Chunk uncompressed size {expected.UncompressedSize} exceeds limit {maxUncompressedBytes}.";
            return false;
        }

        if (!TryReadVerifiedRecordBuffer(handle, fileOffset, expected, out byte[]? rentedBuffer, out int compressedLength, out failureReason))
            return false;

        try
        {
            ReadOnlySpan<byte> compressedSpan = rentedBuffer.AsSpan(ChunkRecordHeaderSize, compressedLength);
            int uncompressedLength = (int)expected.UncompressedSize;
            if (uncompressedLength == 0)
            {
                if (!codec.TryDecompress(compressedSpan, Span<byte>.Empty, out int written) || written != 0)
                {
                    failureReason = $"Failed to decompress 0-byte chunk {expected.Key} with codec {expected.Codec}.";
                    return false;
                }

                return true;
            }

            Span<byte> destinationSpan = uncompressedDestination.GetSpan(uncompressedLength)[..uncompressedLength];
            if (!codec.TryDecompress(compressedSpan, destinationSpan, out int bytesWritten) || bytesWritten != uncompressedLength)
            {
                failureReason = $"Failed to decompress chunk {expected.Key} with codec {expected.Codec} (expected {uncompressedLength} bytes, got {bytesWritten}).";
                return false;
            }

            uncompressedDestination.Advance(bytesWritten);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    public static void WriteChunkIndexEntry(Span<byte> destination, in ChunkIndexEntry entry)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(0, 4), entry.MapId);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(4, 4), entry.ChunkX);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8, 4), entry.ChunkY);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(12, 4), entry.SegmentId);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), entry.RecordOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), entry.CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), entry.UncompressedSize);
        destination[28] = (byte)entry.Codec;
        destination[29] = entry.Flags;
        destination[30] = entry.SchemaVersion;
        destination[31] = entry.Reserved;
    }

    public static ChunkIndexEntry ReadChunkIndexEntry(ReadOnlySpan<byte> source)
    {
        return new ChunkIndexEntry(
            mapId: BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(0, 4)),
            chunkX: BinaryPrimitives.ReadInt32LittleEndian(source.Slice(4, 4)),
            chunkY: BinaryPrimitives.ReadInt32LittleEndian(source.Slice(8, 4)),
            segmentId: BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(12, 4)),
            recordOffset: BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(16, 4)),
            compressedSize: BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(20, 4)),
            uncompressedSize: BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(24, 4)),
            codec: (ChunkCodecId)source[28],
            flags: source[29],
            schemaVersion: source[30],
            reserved: source[31]);
    }

    public static void WriteSnapshotHeader(Span<byte> destination, in SnapshotHeaderData header)
    {
        SnapshotMagicBytes.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), header.FormatVersion);
        destination[6] = (byte)header.CoreCodec;
        destination[7] = (byte)header.Flags;
        WriteGuidRfc4122(destination.Slice(8, 16), header.WorldGuid);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), header.MetaOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28, 4), header.MetaLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(32, 4), header.ThumbOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(36, 4), header.ThumbLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(40, 4), header.CoreOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(44, 4), header.CoreCompressedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(48, 4), header.CoreUncompressedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(52, 4), header.IndexOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(56, 4), header.ChunkIndexCount);
        uint crc32 = Crc32.HashToUInt32(destination[..60]);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(60, 4), crc32);
    }

    public static bool TryReadSnapshotHeader(
        ReadOnlySpan<byte> headerBytes,
        long totalLength,
        out SnapshotHeaderData header,
        [NotNullWhen(false)] out string? failureReason)
    {
        header = default;
        if (headerBytes.Length < SnapshotHeaderSize || totalLength < SnapshotHeaderSize + WholeFileTrailerSize)
        {
            failureReason = "Snapshot is shorter than minimum header and trailer size.";
            return false;
        }

        if (!headerBytes[..4].SequenceEqual(SnapshotMagicBytes))
        {
            failureReason = "Invalid snapshot magic header.";
            return false;
        }

        uint expectedCrc32 = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(60, 4));
        uint actualCrc32 = Crc32.HashToUInt32(headerBytes[..60]);
        if (expectedCrc32 != actualCrc32)
        {
            failureReason = "Snapshot header CRC32 mismatch.";
            return false;
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.Slice(4, 2));
        if (version != SnapshotFormatVersion)
        {
            failureReason = $"Unsupported snapshot version {version}.";
            return false;
        }

        ChunkCodecId coreCodec = (ChunkCodecId)headerBytes[6];
        if (!IsKnownCodec(coreCodec))
        {
            failureReason = $"Unsupported snapshot core codec {(byte)coreCodec}.";
            return false;
        }

        byte rawFlags = headerBytes[7];
        if ((rawFlags & ~(byte)SnapshotFlags.HasThumbnail) != 0)
        {
            failureReason = $"Unknown snapshot header flags 0x{rawFlags:X2}.";
            return false;
        }

        var flags = (SnapshotFlags)rawFlags;
        Guid worldGuid = ReadGuidRfc4122(headerBytes.Slice(8, 16));
        uint metaOffset = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(24, 4));
        uint metaLength = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(28, 4));
        uint thumbOffset = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(32, 4));
        uint thumbLength = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(36, 4));
        uint coreOffset = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(40, 4));
        uint coreCompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(44, 4));
        uint coreUncompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(48, 4));
        uint indexOffset = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(52, 4));
        uint chunkIndexCount = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(56, 4));

        bool hasThumb = (flags & SnapshotFlags.HasThumbnail) != 0;
        if (hasThumb && thumbLength == 0 || !hasThumb && thumbLength != 0)
        {
            failureReason = "Snapshot thumbnail flag conflicts with thumbnail length.";
            return false;
        }

        long payloadEnd = totalLength - WholeFileTrailerSize;
        long metaEnd = (long)metaOffset + metaLength;
        long thumbEnd = (long)thumbOffset + thumbLength;
        long coreEnd = (long)coreOffset + coreCompressedLength;
        long indexBytes = (long)chunkIndexCount * ChunkIndexEntrySize;
        long indexEnd = (long)indexOffset + indexBytes;

        if (metaOffset < SnapshotHeaderSize ||
            thumbOffset < metaEnd ||
            coreOffset < thumbEnd ||
            indexOffset < coreEnd ||
            indexEnd != payloadEnd)
        {
            failureReason = "Snapshot section offsets or lengths are invalid or out of bounds.";
            return false;
        }

        header = new SnapshotHeaderData(
            version,
            coreCodec,
            flags,
            worldGuid,
            metaOffset,
            metaLength,
            thumbOffset,
            thumbLength,
            coreOffset,
            coreCompressedLength,
            coreUncompressedLength,
            indexOffset,
            chunkIndexCount,
            expectedCrc32);

        failureReason = null;
        return true;
    }

    public static bool TryParseAndValidateChunkIndex(
        ReadOnlySpan<byte> indexBytes,
        uint entryCount,
        long dataSectionLength,
        bool requireZeroSegmentId,
        [NotNullWhen(true)] out ChunkIndexEntry[]? entries,
        [NotNullWhen(false)] out string? failureReason)
    {
        entries = null;
        long expectedByteLength = (long)entryCount * ChunkIndexEntrySize;
        if (indexBytes.Length != expectedByteLength)
        {
            failureReason = $"Chunk index length mismatch: expected {expectedByteLength}, got {indexBytes.Length}.";
            return false;
        }

        var parsed = entryCount == 0 ? [] : new ChunkIndexEntry[entryCount];
        for (int i = 0; i < parsed.Length; i++)
        {
            ChunkIndexEntry entry = ReadChunkIndexEntry(indexBytes.Slice(i * ChunkIndexEntrySize, ChunkIndexEntrySize));
            if (!IsKnownCodec(entry.Codec))
            {
                failureReason = $"Chunk index entry {i} has unknown codec {(byte)entry.Codec}.";
                return false;
            }

            if (entry.Reserved != 0)
            {
                failureReason = $"Chunk index entry {i} has non-zero reserved byte.";
                return false;
            }

            if (requireZeroSegmentId)
            {
                if (entry.SegmentId != 0)
                {
                    failureReason = $"Archive chunk index entry {i} has non-zero segment ID {entry.SegmentId}.";
                    return false;
                }

                long recordEnd = (long)entry.RecordOffset + ChunkRecordHeaderSize + entry.CompressedSize + ChunkRecordTrailerSize;
                if (recordEnd > dataSectionLength)
                {
                    failureReason = $"Archive chunk index entry {i} exceeds archive chunk data section bounds.";
                    return false;
                }
            }
            else if (entry.SegmentId == 0)
            {
                failureReason = $"Snapshot chunk index entry {i} has invalid segment ID 0.";
                return false;
            }

            if (i > 0 && entry.Key.CompareTo(parsed[i - 1].Key) <= 0)
            {
                failureReason = $"Chunk index entry {i} is unsorted or duplicates key {entry.Key}.";
                return false;
            }

            parsed[i] = entry;
        }

        entries = parsed;
        failureReason = null;
        return true;
    }

    public static void WriteAsavHeader(
        Span<byte> destination,
        Guid worldGuid,
        uint snapshotByteLength,
        uint chunkDataOffset)
    {
        AsavMagicBytes.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), AsavFormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), 0);
        WriteGuidRfc4122(destination.Slice(8, 16), worldGuid);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(24, 4), snapshotByteLength);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(28, 4), chunkDataOffset);
    }

    public static bool TryReadAsavHeader(
        ReadOnlySpan<byte> headerBytes,
        long totalFileLength,
        out Guid worldGuid,
        out uint snapshotByteLength,
        out uint chunkDataOffset,
        [NotNullWhen(false)] out string? failureReason)
    {
        worldGuid = Guid.Empty;
        snapshotByteLength = 0;
        chunkDataOffset = 0;

        if (headerBytes.Length < AsavHeaderSize || totalFileLength < AsavHeaderSize + WholeFileTrailerSize)
        {
            failureReason = "Archive is shorter than minimum ASAV header and trailer size.";
            return false;
        }

        if (!headerBytes[..4].SequenceEqual(AsavMagicBytes))
        {
            failureReason = "Invalid ASAV magic header.";
            return false;
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.Slice(4, 2));
        if (version != AsavFormatVersion)
        {
            failureReason = $"Unsupported ASAV version {version}.";
            return false;
        }

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(headerBytes.Slice(6, 2));
        if (flags != 0)
        {
            failureReason = $"Unsupported ASAV flags 0x{flags:X4}.";
            return false;
        }

        worldGuid = ReadGuidRfc4122(headerBytes.Slice(8, 16));
        snapshotByteLength = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(24, 4));
        chunkDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(28, 4));

        long expectedChunkDataOffset = Align16((long)AsavHeaderSize + snapshotByteLength);
        if (chunkDataOffset != expectedChunkDataOffset ||
            chunkDataOffset > totalFileLength - WholeFileTrailerSize)
        {
            failureReason = "ASAV snapshot length or chunk data offset is out of bounds.";
            return false;
        }

        failureReason = null;
        return true;
    }

    public static bool TryVerifyWholeFileXxHash64(
        IRandomAccessHandle handle,
        [NotNullWhen(false)] out string? failureReason)
    {
        long length = handle.Length;
        if (length < WholeFileTrailerSize)
        {
            failureReason = "File is shorter than 8-byte XxHash64 trailer.";
            return false;
        }

        long payloadLength = length - WholeFileTrailerSize;
        if (!TryComputeStreamingXxHash64(handle, payloadLength, out ulong actualHash, out failureReason))
            return false;

        Span<byte> trailer = stackalloc byte[WholeFileTrailerSize];
        if (handle.ReadAt(payloadLength, trailer) != WholeFileTrailerSize)
        {
            failureReason = "Failed to read trailing XxHash64.";
            return false;
        }

        ulong expectedHash = BinaryPrimitives.ReadUInt64LittleEndian(trailer);
        if (actualHash != expectedHash)
        {
            failureReason = $"Whole-file XxHash64 mismatch: expected 0x{expectedHash:X16}, actual 0x{actualHash:X16}.";
            return false;
        }

        failureReason = null;
        return true;
    }

    public static bool TryComputeStreamingXxHash64(
        IRandomAccessHandle handle,
        long byteCount,
        out ulong hash,
        [NotNullWhen(false)] out string? failureReason)
    {
        const int BufferSize = 64 * 1024;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            var hasher = new XxHash64();
            long offset = 0;
            while (offset < byteCount)
            {
                int toRead = (int)Math.Min(BufferSize, byteCount - offset);
                int read = handle.ReadAt(offset, buffer.AsSpan(0, toRead));
                if (read != toRead)
                {
                    hash = 0;
                    failureReason = "Unexpected end of file while computing XxHash64.";
                    return false;
                }

                hasher.Append(buffer.AsSpan(0, read));
                offset += read;
            }

            hash = hasher.GetCurrentHashAsUInt64();
            failureReason = null;
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static bool IsKnownCodec(ChunkCodecId codec) =>
        codec is ChunkCodecId.None or ChunkCodecId.Lz4 or ChunkCodecId.Zstd;

    private static bool TryReadVerifiedRecordBuffer(
        IRandomAccessHandle handle,
        long fileOffset,
        in ChunkIndexEntry expected,
        [NotNullWhen(true)] out byte[]? rentedBuffer,
        out int compressedLength,
        [NotNullWhen(false)] out string? failureReason)
    {
        rentedBuffer = null;
        compressedLength = 0;

        if (expected.Reserved != 0)
        {
            failureReason = $"Chunk {expected.Key} index entry has non-zero reserved byte.";
            return false;
        }

        if (fileOffset < 0 || expected.CompressedSize > int.MaxValue - ChunkRecordHeaderSize - ChunkRecordTrailerSize)
        {
            failureReason = $"Chunk {expected.Key} offset or compressed size is out of bounds.";
            return false;
        }

        long crcInputLengthLong = ChunkRecordHeaderSize + (long)expected.CompressedSize;
        long totalRecordBytesLong = crcInputLengthLong + ChunkRecordTrailerSize;
        if (fileOffset + totalRecordBytesLong > handle.Length)
        {
            failureReason = $"Chunk {expected.Key} record at offset {fileOffset} exceeds file length {handle.Length}.";
            return false;
        }

        int crcInputLength = (int)crcInputLengthLong;
        int totalRecordBytes = (int)totalRecordBytesLong;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(totalRecordBytes);
        bool keepBuffer = false;
        try
        {
            Span<byte> recordSpan = buffer.AsSpan(0, totalRecordBytes);
            int bytesRead = handle.ReadAt(fileOffset, recordSpan);
            if (bytesRead != totalRecordBytes)
            {
                failureReason = $"Short read for chunk {expected.Key} at offset {fileOffset}: expected {totalRecordBytes}, got {bytesRead}.";
                return false;
            }

            ulong expectedCrc = BinaryPrimitives.ReadUInt64LittleEndian(recordSpan.Slice(crcInputLength, ChunkRecordTrailerSize));
            ulong actualCrc = XxHash64.HashToUInt64(recordSpan[..crcInputLength]);
            if (expectedCrc != actualCrc)
            {
                failureReason = $"Chunk {expected.Key} XxHash64 mismatch at offset {fileOffset}.";
                return false;
            }

            if (!recordSpan[..4].SequenceEqual(ChunkRecordMagicBytes))
            {
                failureReason = $"Invalid chunk record magic at offset {fileOffset}.";
                return false;
            }

            uint mapId = BinaryPrimitives.ReadUInt32LittleEndian(recordSpan.Slice(4, 4));
            int chunkX = BinaryPrimitives.ReadInt32LittleEndian(recordSpan.Slice(8, 4));
            int chunkY = BinaryPrimitives.ReadInt32LittleEndian(recordSpan.Slice(12, 4));
            uint compSize = BinaryPrimitives.ReadUInt32LittleEndian(recordSpan.Slice(16, 4));
            uint uncompSize = BinaryPrimitives.ReadUInt32LittleEndian(recordSpan.Slice(20, 4));
            byte codec = recordSpan[24];
            byte schemaVersion = recordSpan[25];
            ushort rawFlags = BinaryPrimitives.ReadUInt16LittleEndian(recordSpan.Slice(26, 2));
            uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(recordSpan.Slice(28, 4));

            if (mapId != expected.MapId ||
                chunkX != expected.ChunkX ||
                chunkY != expected.ChunkY ||
                compSize != expected.CompressedSize ||
                uncompSize != expected.UncompressedSize ||
                codec != (byte)expected.Codec ||
                schemaVersion != expected.SchemaVersion ||
                (rawFlags & 0xFF) != expected.Flags ||
                (rawFlags >> 8) != 0 ||
                reserved != 0)
            {
                failureReason = $"Chunk record header metadata mismatch at offset {fileOffset} for {expected.Key}.";
                return false;
            }

            compressedLength = (int)expected.CompressedSize;
            rentedBuffer = buffer;
            keepBuffer = true;
            failureReason = null;
            return true;
        }
        finally
        {
            if (!keepBuffer)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
