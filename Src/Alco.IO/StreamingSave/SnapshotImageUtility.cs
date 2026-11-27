using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;

namespace Alco.IO;

internal static class SnapshotImageUtility
{
    public static ChunkIndexEntry[] SortAndValidateChunkIndex(ReadOnlySpan<ChunkIndexEntry> entries)
    {
        if (entries.IsEmpty)
            return [];

        ChunkIndexEntry[] sorted = entries.ToArray();
        Array.Sort(sorted, static (a, b) => a.Key.CompareTo(b.Key));
        for (int i = 0; i < sorted.Length; i++)
        {
            if (!StreamingSaveFormat.IsKnownCodec(sorted[i].Codec))
                throw new ArgumentException($"Chunk index entry {i} has unsupported codec {(byte)sorted[i].Codec}.", nameof(entries));

            if (sorted[i].SegmentId == 0)
                throw new ArgumentException($"Snapshot chunk index entry {i} ({sorted[i].Key}) must reference a committed segment (>= 1).", nameof(entries));

            if (sorted[i].Reserved != 0)
                throw new ArgumentException($"Chunk index entry {i} has non-zero reserved byte.", nameof(entries));

            if (i > 0 && sorted[i].Key == sorted[i - 1].Key)
                throw new ArgumentException($"Duplicate chunk key {sorted[i].Key} in snapshot index.", nameof(entries));
        }

        return sorted;
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
        IChunkCodec codec = codecs.GetCodec(coreCodec);
        byte[] metaBytes = PackedBinarySerializer.Encode(meta, elideDefaults: false);

        int maxCoreComp = codec.GetMaxCompressedLength(coreUncompressed.Length);
        byte[]? compCoreBuf = maxCoreComp > 0 ? ArrayPool<byte>.Shared.Rent(maxCoreComp) : null;
        try
        {
            Span<byte> compCoreSpan = compCoreBuf is not null ? compCoreBuf.AsSpan(0, maxCoreComp) : Span<byte>.Empty;
            int coreCompLen = codec.Compress(coreUncompressed, compCoreSpan);

            int totalLength = ComputeSnapshotByteLength(
                metaBytes.Length,
                thumbnailPng.Length,
                coreCompLen,
                sortedChunkIndex.Length);

            byte[] image = new byte[totalLength];
            WriteSnapshotImageCore(
                image,
                worldGuid,
                metaBytes,
                thumbnailPng,
                compCoreSpan[..coreCompLen],
                (uint)coreUncompressed.Length,
                coreCodec,
                sortedChunkIndex);
            return image;
        }
        finally
        {
            if (compCoreBuf is not null)
                ArrayPool<byte>.Shared.Return(compCoreBuf);
        }
    }

    public static int ComputeSnapshotByteLength(
        int metaLength,
        int thumbnailLength,
        int compressedCoreLength,
        int chunkIndexCount)
    {
        long total = (long)StreamingSaveFormat.SnapshotHeaderSize +
                     metaLength +
                     thumbnailLength +
                     compressedCoreLength +
                     (long)chunkIndexCount * StreamingSaveFormat.ChunkIndexEntrySize +
                     StreamingSaveFormat.WholeFileTrailerSize;
        return checked((int)total);
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
        uint metaOffset = StreamingSaveFormat.SnapshotHeaderSize;
        uint thumbOffset = checked(metaOffset + (uint)metaBytes.Length);
        uint coreOffset = checked(thumbOffset + (uint)thumbnailPng.Length);
        uint indexOffset = checked(coreOffset + (uint)compressedCoreBytes.Length);
        uint indexBytes = checked((uint)sortedChunkIndex.Length * StreamingSaveFormat.ChunkIndexEntrySize);
        uint trailerOffset = checked(indexOffset + indexBytes);
        int expectedLength = checked((int)(trailerOffset + StreamingSaveFormat.WholeFileTrailerSize));

        if (destination.Length < expectedLength)
            throw new ArgumentException("Destination buffer is smaller than computed snapshot image length.", nameof(destination));

        var header = new SnapshotHeaderData(
            StreamingSaveFormat.SnapshotFormatVersion,
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

        StreamingSaveFormat.WriteSnapshotHeader(destination, header);
        metaBytes.CopyTo(destination.Slice((int)metaOffset, metaBytes.Length));
        if (!thumbnailPng.IsEmpty)
            thumbnailPng.CopyTo(destination.Slice((int)thumbOffset, thumbnailPng.Length));
        if (!compressedCoreBytes.IsEmpty)
            compressedCoreBytes.CopyTo(destination.Slice((int)coreOffset, compressedCoreBytes.Length));

        for (int i = 0; i < sortedChunkIndex.Length; i++)
        {
            Span<byte> entryDst = destination.Slice(
                (int)indexOffset + i * StreamingSaveFormat.ChunkIndexEntrySize,
                StreamingSaveFormat.ChunkIndexEntrySize);
            StreamingSaveFormat.WriteChunkIndexEntry(entryDst, sortedChunkIndex[i]);
        }

        ulong wholeHash = XxHash64.HashToUInt64(destination[..(int)trailerOffset]);
        BinaryPrimitives.WriteUInt64LittleEndian(
            destination.Slice((int)trailerOffset, StreamingSaveFormat.WholeFileTrailerSize),
            wholeHash);
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

        ReadOnlySpan<byte> span = snapMemory.Span;
        if (verifyWholeImageHash)
        {
            if (span.Length < StreamingSaveFormat.SnapshotHeaderSize + StreamingSaveFormat.WholeFileTrailerSize)
            {
                failureReason = "Snapshot image is shorter than minimum size.";
                return false;
            }

            int payloadLen = span.Length - StreamingSaveFormat.WholeFileTrailerSize;
            ulong expectedHash = BinaryPrimitives.ReadUInt64LittleEndian(span[payloadLen..]);
            ulong actualHash = XxHash64.HashToUInt64(span[..payloadLen]);
            if (expectedHash != actualHash)
            {
                failureReason = $"Snapshot XxHash64 mismatch: expected 0x{expectedHash:X16}, actual 0x{actualHash:X16}.";
                return false;
            }
        }

        if (!StreamingSaveFormat.TryReadSnapshotHeader(span, span.Length, out header, out failureReason))
            return false;

        if (expectedWorldGuid.HasValue && header.WorldGuid != expectedWorldGuid.Value)
        {
            failureReason = $"Snapshot world GUID mismatch: expected {expectedWorldGuid.Value:N}, got {header.WorldGuid:N}.";
            return false;
        }

        if (header.CoreUncompressedLength > (uint)maxUncompressedCoreBytes)
        {
            failureReason = $"Snapshot core size {header.CoreUncompressedLength} exceeds limit {maxUncompressedCoreBytes}.";
            return false;
        }

        if (!codecs.TryGetCodec(header.CoreCodec, out IChunkCodec? coreCodec))
        {
            failureReason = $"Unsupported snapshot core codec {(byte)header.CoreCodec}.";
            return false;
        }

        if (!TryDecodeMeta(
                snapMemory.Slice((int)header.MetaOffset, (int)header.MetaLength),
                out meta,
                out failureReason))
        {
            return false;
        }

        ReadOnlySpan<byte> coreCompressed = span.Slice((int)header.CoreOffset, (int)header.CoreCompressedLength);
        int coreUncompLen = (int)header.CoreUncompressedLength;
        byte[] decodedCore = coreUncompLen == 0 ? [] : new byte[coreUncompLen];
        if (!coreCodec.TryDecompress(coreCompressed, decodedCore, out int written) || written != coreUncompLen)
        {
            meta = default;
            failureReason = $"Failed to decompress snapshot core payload with codec {header.CoreCodec}.";
            return false;
        }

        ReadOnlySpan<byte> indexSpan = span.Slice(
            (int)header.IndexOffset,
            checked((int)((long)header.ChunkIndexCount * StreamingSaveFormat.ChunkIndexEntrySize)));
        if (!StreamingSaveFormat.TryParseAndValidateChunkIndex(
                indexSpan,
                header.ChunkIndexCount,
                archiveChunkDataLength,
                requireZeroSegmentId,
                out chunkIndex,
                out failureReason))
        {
            meta = default;
            return false;
        }

        if (includeThumbnail && (header.Flags & SnapshotFlags.HasThumbnail) != 0 && header.ThumbLength > 0)
            thumbnailBytes = span.Slice((int)header.ThumbOffset, (int)header.ThumbLength).ToArray();

        coreBytes = decodedCore;
        failureReason = null;
        return true;
    }
}
