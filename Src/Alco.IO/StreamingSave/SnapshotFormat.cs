using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;

namespace Alco.IO;

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

internal readonly record struct RawSnapshotImage(
    SnapshotHeaderData Header,
    ReadOnlyMemory<byte> MetaBytes,
    ReadOnlyMemory<byte> ThumbBytes,
    ReadOnlyMemory<byte> CompressedCoreBytes,
    ChunkIndexEntry[] ChunkIndex);

internal static class SnapshotFormat
{
    public const int SnapshotHeaderSize = 64;
    public const ushort SnapshotFormatVersion = 1;
    public const int ChunkIndexEntrySize = 32;
    public const int WholeFileTrailerSize = 8;
    public const int DefaultMaxUncompressedCoreBytes = 256 * 1024 * 1024;

    public static ReadOnlySpan<byte> SnapshotMagicBytes => "ASNP"u8;

    public static ChunkIndexEntry[] SortAndValidateChunkIndex(ReadOnlySpan<ChunkIndexEntry> entries)
    {
        if (entries.IsEmpty)
            return [];

        ChunkIndexEntry[] sorted = entries.ToArray();
        Array.Sort(sorted, static (a, b) => a.Key.CompareTo(b.Key));
        for (int i = 0; i < sorted.Length; i++)
        {
            if (!ChunkCodecSet.IsValid(sorted[i].Codec))
                throw new ArgumentException($"Chunk index entry {i} has unsupported codec {(byte)sorted[i].Codec}.", nameof(entries));

            if (sorted[i].SegmentId == 0)
                throw new ArgumentException($"Snapshot chunk index entry {i} ({sorted[i].Key}) must reference a committed segment (>= 1).", nameof(entries));

            if (i > 0 && sorted[i].Key == sorted[i - 1].Key)
                throw new ArgumentException($"Duplicate chunk key {sorted[i].Key} in snapshot index.", nameof(entries));
        }

        return sorted;
    }

    public static int ComputeSnapshotByteLength(
        int metaLength,
        int thumbnailLength,
        int compressedCoreLength,
        int chunkIndexCount)
    {
        long total = (long)SnapshotHeaderSize +
                     metaLength +
                     thumbnailLength +
                     compressedCoreLength +
                     (long)chunkIndexCount * ChunkIndexEntrySize +
                     WholeFileTrailerSize;
        return checked((int)total);
    }

    public static byte[] BuildSnapshotImage<TMeta>(
        Guid worldGuid,
        TMeta meta,
        ReadOnlySpan<byte> coreUncompressed,
        ReadOnlySpan<ChunkIndexEntry> sortedChunkIndex,
        ReadOnlySpan<byte> thumbnailPng,
        ChunkCodecId coreCodec,
        ChunkCodecSet codecs) where TMeta : ISerializable
    {
        byte[] metaBytes = PackedBinarySerializer.Encode(meta);
        int maxCoreComp = codecs.GetMaxCompressedLength(coreCodec, coreUncompressed.Length);
        byte[]? compCoreBuf = maxCoreComp > 0 ? ArrayPool<byte>.Shared.Rent(maxCoreComp) : null;
        try
        {
            Span<byte> compCoreSpan = compCoreBuf is not null ? compCoreBuf.AsSpan(0, maxCoreComp) : Span<byte>.Empty;
            int coreCompLen = codecs.Compress(coreCodec, coreUncompressed, compCoreSpan);

            return BuildFromRawSections(
                worldGuid,
                metaBytes,
                thumbnailPng,
                compCoreSpan[..coreCompLen],
                checked((uint)coreUncompressed.Length),
                coreCodec,
                sortedChunkIndex);
        }
        finally
        {
            if (compCoreBuf is not null)
                ArrayPool<byte>.Shared.Return(compCoreBuf);
        }
    }

    public static byte[] BuildFromRawSections(
        Guid worldGuid,
        ReadOnlySpan<byte> metaBytes,
        ReadOnlySpan<byte> thumbnailPng,
        ReadOnlySpan<byte> compressedCoreBytes,
        uint coreUncompressedLength,
        ChunkCodecId coreCodec,
        ReadOnlySpan<ChunkIndexEntry> sortedChunkIndex)
    {
        int totalLength = ComputeSnapshotByteLength(
            metaBytes.Length,
            thumbnailPng.Length,
            compressedCoreBytes.Length,
            sortedChunkIndex.Length);

        byte[] image = new byte[totalLength];
        WriteSnapshotImageCore(
            image,
            worldGuid,
            metaBytes,
            thumbnailPng,
            compressedCoreBytes,
            coreUncompressedLength,
            coreCodec,
            sortedChunkIndex);
        return image;
    }

    public static void WriteSnapshotImageCore(
        Span<byte> destination,
        Guid worldGuid,
        ReadOnlySpan<byte> metaBytes,
        ReadOnlySpan<byte> thumbnailPng,
        ReadOnlySpan<byte> compressedCoreBytes,
        uint coreUncompressedLength,
        ChunkCodecId coreCodec,
        ReadOnlySpan<ChunkIndexEntry> sortedChunkIndex)
    {
        uint metaOffset = SnapshotHeaderSize;
        uint thumbOffset = checked(metaOffset + (uint)metaBytes.Length);
        uint coreOffset = checked(thumbOffset + (uint)thumbnailPng.Length);
        uint indexOffset = checked(coreOffset + (uint)compressedCoreBytes.Length);
        uint indexBytes = checked((uint)sortedChunkIndex.Length * ChunkIndexEntrySize);
        uint trailerOffset = checked(indexOffset + indexBytes);
        int expectedLength = checked((int)(trailerOffset + WholeFileTrailerSize));

        if (destination.Length < expectedLength)
            throw new ArgumentException("Destination buffer is smaller than computed snapshot image length.", nameof(destination));

        var header = new SnapshotHeaderData(
            SnapshotFormatVersion,
            coreCodec,
            thumbnailPng.IsEmpty ? SnapshotFlags.None : SnapshotFlags.HasThumbnail,
            worldGuid,
            metaOffset,
            checked((uint)metaBytes.Length),
            thumbOffset,
            checked((uint)thumbnailPng.Length),
            coreOffset,
            checked((uint)compressedCoreBytes.Length),
            coreUncompressedLength,
            indexOffset,
            checked((uint)sortedChunkIndex.Length),
            HeaderCrc32: 0);

        WriteSnapshotHeader(destination, header);
        metaBytes.CopyTo(destination.Slice((int)metaOffset, metaBytes.Length));
        if (!thumbnailPng.IsEmpty)
            thumbnailPng.CopyTo(destination.Slice((int)thumbOffset, thumbnailPng.Length));
        if (!compressedCoreBytes.IsEmpty)
            compressedCoreBytes.CopyTo(destination.Slice((int)coreOffset, compressedCoreBytes.Length));

        for (int i = 0; i < sortedChunkIndex.Length; i++)
        {
            Span<byte> entryDst = destination.Slice((int)indexOffset + i * ChunkIndexEntrySize, ChunkIndexEntrySize);
            WriteChunkIndexEntry(entryDst, sortedChunkIndex[i]);
        }

        ulong wholeHash = XxHash64.HashToUInt64(destination[..(int)trailerOffset]);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice((int)trailerOffset, WholeFileTrailerSize), wholeHash);
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
        destination[31] = 0;
    }

    public static void WriteSnapshotHeader(Span<byte> destination, in SnapshotHeaderData header)
    {
        SnapshotMagicBytes.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), header.FormatVersion);
        destination[6] = (byte)header.CoreCodec;
        destination[7] = (byte)header.Flags;
        ChunkRecordCodec.WriteGuidRfc4122(destination.Slice(8, 16), header.WorldGuid);
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

        var coreCodec = (ChunkCodecId)headerBytes[6];
        if (!ChunkCodecSet.IsValid(coreCodec))
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
        Guid worldGuid = ChunkRecordCodec.ReadGuidRfc4122(headerBytes.Slice(8, 16));
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

        ChunkIndexEntry[] parsed = entryCount == 0 ? [] : new ChunkIndexEntry[entryCount];
        for (int i = 0; i < parsed.Length; i++)
        {
            ReadOnlySpan<byte> slice = indexBytes.Slice(i * ChunkIndexEntrySize, ChunkIndexEntrySize);
            if (slice[31] != 0)
            {
                failureReason = $"Chunk index entry {i} has non-zero reserved byte.";
                return false;
            }

            ChunkIndexEntry entry = ReadChunkIndexEntry(slice);
            if (!ChunkCodecSet.IsValid(entry.Codec))
            {
                failureReason = $"Chunk index entry {i} has unknown codec {(byte)entry.Codec}.";
                return false;
            }

            if (requireZeroSegmentId)
            {
                if (entry.SegmentId != 0)
                {
                    failureReason = $"Archive chunk index entry {i} has non-zero segment ID {entry.SegmentId}.";
                    return false;
                }

                long recordEnd = (long)entry.RecordOffset + ChunkRecordCodec.ChunkRecordHeaderSize + entry.CompressedSize + ChunkRecordCodec.ChunkRecordTrailerSize;
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

    public static bool TryReadRawSnapshot(
        ReadOnlyMemory<byte> snapMemory,
        Guid? expectedWorldGuid,
        bool verifyWholeImageHash,
        bool requireZeroSegmentId,
        long archiveChunkDataLength,
        out RawSnapshotImage raw,
        [NotNullWhen(false)] out string? failureReason)
    {
        raw = default;
        ReadOnlySpan<byte> span = snapMemory.Span;
        if (verifyWholeImageHash)
        {
            if (span.Length < SnapshotHeaderSize + WholeFileTrailerSize)
            {
                failureReason = "Snapshot image is shorter than minimum size.";
                return false;
            }

            int payloadLen = span.Length - WholeFileTrailerSize;
            ulong expectedHash = BinaryPrimitives.ReadUInt64LittleEndian(span[payloadLen..]);
            ulong actualHash = XxHash64.HashToUInt64(span[..payloadLen]);
            if (expectedHash != actualHash)
            {
                failureReason = $"Snapshot XxHash64 mismatch: expected 0x{expectedHash:X16}, actual 0x{actualHash:X16}.";
                return false;
            }
        }

        if (!TryReadSnapshotHeader(span, span.Length, out SnapshotHeaderData header, out failureReason))
            return false;

        if (expectedWorldGuid.HasValue && header.WorldGuid != expectedWorldGuid.Value)
        {
            failureReason = $"Snapshot world GUID mismatch: expected {expectedWorldGuid.Value:N}, got {header.WorldGuid:N}.";
            return false;
        }

        ReadOnlySpan<byte> indexSpan = span.Slice(
            (int)header.IndexOffset,
            checked((int)((long)header.ChunkIndexCount * ChunkIndexEntrySize)));
        if (!TryParseAndValidateChunkIndex(
                indexSpan,
                header.ChunkIndexCount,
                archiveChunkDataLength,
                requireZeroSegmentId,
                out ChunkIndexEntry[]? chunkIndex,
                out failureReason))
        {
            return false;
        }

        ReadOnlyMemory<byte> metaBytes = snapMemory.Slice((int)header.MetaOffset, (int)header.MetaLength);
        ReadOnlyMemory<byte> thumbBytes = (header.Flags & SnapshotFlags.HasThumbnail) != 0 && header.ThumbLength > 0
            ? snapMemory.Slice((int)header.ThumbOffset, (int)header.ThumbLength)
            : ReadOnlyMemory<byte>.Empty;
        ReadOnlyMemory<byte> compressedCoreBytes = snapMemory.Slice((int)header.CoreOffset, (int)header.CoreCompressedLength);

        raw = new RawSnapshotImage(header, metaBytes, thumbBytes, compressedCoreBytes, chunkIndex);
        failureReason = null;
        return true;
    }

    public static bool TryDecodeMeta<TMeta>(
        ReadOnlyMemory<byte> metaMemory,
        [NotNullWhen(true)] out TMeta? meta,
        [NotNullWhen(false)] out string? failureReason) where TMeta : ISerializable, new()
    {
        string? decodeError = null;
        try
        {
            if (PackedBinarySerializer.TryDecode(metaMemory, static _ => new TMeta(), out meta, onError: err => decodeError = err))
            {
                failureReason = null;
                return true;
            }

            failureReason = decodeError ?? "Failed to decode snapshot PackedBinary metadata.";
            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            meta = default;
            failureReason = $"Failed to decode snapshot metadata: {ex.Message}";
            return false;
        }
    }

    public static bool TryParseSnapshotImage<TMeta>(
        ReadOnlyMemory<byte> snapMemory,
        Guid? expectedWorldGuid,
        ChunkCodecSet codecs,
        int maxUncompressedCoreBytes,
        bool verifyWholeImageHash,
        bool requireZeroSegmentId,
        long archiveChunkDataLength,
        bool includeThumbnail,
        out SnapshotHeaderData header,
        [NotNullWhen(true)] out TMeta? meta,
        [NotNullWhen(true)] out byte[]? coreBytes,
        [NotNullWhen(true)] out ChunkIndexEntry[]? chunkIndex,
        out byte[]? thumbnailBytes,
        [NotNullWhen(false)] out string? failureReason) where TMeta : ISerializable, new()
    {
        header = default;
        meta = default;
        coreBytes = null;
        chunkIndex = null;
        thumbnailBytes = null;

        if (!TryReadRawSnapshot(
                snapMemory,
                expectedWorldGuid,
                verifyWholeImageHash,
                requireZeroSegmentId,
                archiveChunkDataLength,
                out RawSnapshotImage raw,
                out failureReason))
        {
            return false;
        }

        header = raw.Header;
        if (header.CoreUncompressedLength > (uint)maxUncompressedCoreBytes)
        {
            failureReason = $"Snapshot core size {header.CoreUncompressedLength} exceeds limit {maxUncompressedCoreBytes}.";
            return false;
        }

        if (!TryDecodeMeta(raw.MetaBytes, out meta, out failureReason))
            return false;

        int coreUncompLen = (int)header.CoreUncompressedLength;
        byte[] decodedCore = coreUncompLen == 0 ? [] : new byte[coreUncompLen];
        if (!codecs.TryDecompress(header.CoreCodec, raw.CompressedCoreBytes.Span, decodedCore, out int written) || written != coreUncompLen)
        {
            meta = default;
            failureReason = $"Failed to decompress snapshot core payload with codec {header.CoreCodec}.";
            return false;
        }

        if (includeThumbnail && !raw.ThumbBytes.IsEmpty)
            thumbnailBytes = raw.ThumbBytes.ToArray();

        coreBytes = decodedCore;
        chunkIndex = raw.ChunkIndex;
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

    private static ChunkIndexEntry ReadChunkIndexEntry(ReadOnlySpan<byte> source)
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
            schemaVersion: source[30]);
    }
}
