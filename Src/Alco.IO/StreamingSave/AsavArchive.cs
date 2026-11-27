using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Alco.IO;

/// <summary>
/// Exports and imports self-contained portable <c>.asav</c> world save archives.
/// </summary>
public static class AsavArchive
{
    private const int MaxImportSlabBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Exports a snapshot and all of its live chunks into an external <c>.asav</c> file atomically.
    /// </summary>
    /// <typeparam name="TMeta">The snapshot metadata type.</typeparam>
    /// <param name="snapshotStore">The snapshot store holding the source snapshot.</param>
    /// <param name="sourceId">The source snapshot identifier.</param>
    /// <param name="segmentStore">The segment store holding the snapshot's chunks.</param>
    /// <param name="destinationFilePath">The destination <c>.asav</c> file path.</param>
    /// <param name="codecs">The shared codec set used for compression and transcoding.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <param name="chunkCodec">The codec used to encode chunks inside the archive (default <see cref="ChunkCodecId.Zstd"/>).</param>
    /// <param name="coreCodec">The codec used to encode the embedded core payload (default <see cref="ChunkCodecId.Zstd"/>).</param>
    /// <returns><see langword="true"/> if exported atomically; otherwise, <see langword="false"/>.</returns>
    public static bool TryExportToFile<TMeta>(
        SnapshotStore<TMeta> snapshotStore,
        SnapshotId sourceId,
        ChunkSegmentStore segmentStore,
        string destinationFilePath,
        ChunkCodecSet codecs,
        [NotNullWhen(false)] out string? failureReason,
        ChunkCodecId chunkCodec = ChunkCodecId.Zstd,
        ChunkCodecId coreCodec = ChunkCodecId.Zstd) where TMeta : ISerializable, new()
    {
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(segmentStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);
        ArgumentNullException.ThrowIfNull(codecs);

        string fullDestPath = Path.GetFullPath(destinationFilePath);
        return DiskFileUtility.TryWriteAtomic(
            fullDestPath,
            (IRandomAccessHandle handle, [NotNullWhen(false)] out string? reason) => TryExport(
                snapshotStore,
                sourceId,
                segmentStore,
                handle,
                codecs,
                out reason,
                chunkCodec,
                coreCodec,
                flushToDisk: false),
            out failureReason,
            flushToDisk: true);
    }

    /// <summary>
    /// Exports a snapshot and all of its live chunks into an open writable <see cref="IRandomAccessHandle"/> in <c>.asav</c> format.
    /// </summary>
    /// <typeparam name="TMeta">The snapshot metadata type.</typeparam>
    /// <param name="snapshotStore">The snapshot store holding the source snapshot.</param>
    /// <param name="sourceId">The source snapshot identifier.</param>
    /// <param name="segmentStore">The segment store holding the snapshot's chunks.</param>
    /// <param name="outputHandle">The writable output handle.</param>
    /// <param name="codecs">The shared codec set used for compression and transcoding.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <param name="chunkCodec">The codec used to encode chunks inside the archive (default <see cref="ChunkCodecId.Zstd"/>).</param>
    /// <param name="coreCodec">The codec used to encode the embedded core payload (default <see cref="ChunkCodecId.Zstd"/>).</param>
    /// <param name="flushToDisk">Whether to flush the output handle when complete.</param>
    /// <returns><see langword="true"/> if exported cleanly; otherwise, <see langword="false"/>.</returns>
    public static bool TryExport<TMeta>(
        SnapshotStore<TMeta> snapshotStore,
        SnapshotId sourceId,
        ChunkSegmentStore segmentStore,
        IRandomAccessHandle outputHandle,
        ChunkCodecSet codecs,
        [NotNullWhen(false)] out string? failureReason,
        ChunkCodecId chunkCodec = ChunkCodecId.Zstd,
        ChunkCodecId coreCodec = ChunkCodecId.Zstd,
        bool flushToDisk = true) where TMeta : ISerializable, new()
    {
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(segmentStore);
        ArgumentNullException.ThrowIfNull(outputHandle);
        ArgumentNullException.ThrowIfNull(codecs);

        if (segmentStore.WorldGuid != sourceId.WorldGuid)
            throw new ArgumentException("Segment store world GUID does not match source snapshot.", nameof(segmentStore));
        if (!ChunkCodecSet.IsValid(chunkCodec))
            throw new ArgumentOutOfRangeException(nameof(chunkCodec), $"Unsupported chunk codec {(byte)chunkCodec}.");
        if (!ChunkCodecSet.IsValid(coreCodec))
            throw new ArgumentOutOfRangeException(nameof(coreCodec), $"Unsupported core codec {(byte)coreCodec}.");

        string relPath = StreamingSavePaths.SnapshotFilePath(sourceId.WorldGuid, sourceId.SlotId);
        if (!snapshotStore.Storage.TryReadAllBytes(relPath, out byte[]? snapBytes))
        {
            failureReason = $"Snapshot '{relPath}' does not exist.";
            return false;
        }

        if (!SnapshotFormat.TryReadRawSnapshot(
                snapBytes,
                sourceId.WorldGuid,
                verifyWholeImageHash: true,
                requireZeroSegmentId: false,
                archiveChunkDataLength: 0,
                out RawSnapshotImage raw,
                out failureReason))
        {
            return false;
        }

        byte[]? transcodedCoreBuf = null;
        try
        {
            ReadOnlySpan<byte> compCoreSpan;
            if (raw.Header.CoreCodec == coreCodec)
            {
                compCoreSpan = raw.CompressedCoreBytes.Span;
            }
            else
            {
                if (!TryTranscodeCore(
                        raw.CompressedCoreBytes.Span,
                        raw.Header.CoreCodec,
                        (int)raw.Header.CoreUncompressedLength,
                        snapshotStore.MaxUncompressedCoreBytes,
                        coreCodec,
                        codecs,
                        out transcodedCoreBuf,
                        out int compCoreLen,
                        out failureReason))
                {
                    return false;
                }

                compCoreSpan = transcodedCoreBuf.AsSpan(0, compCoreLen);
            }

            int snapByteLen = SnapshotFormat.ComputeSnapshotByteLength(
                raw.MetaBytes.Length,
                raw.ThumbBytes.Length,
                compCoreSpan.Length,
                raw.ChunkIndex.Length);
            uint chunkDataOffset = checked((uint)ChunkRecordCodec.Align16((long)AsavFormat.AsavHeaderSize + snapByteLen));

            if (!TryWriteArchiveChunks(
                    segmentStore,
                    raw.ChunkIndex,
                    chunkCodec,
                    codecs,
                    outputHandle,
                    chunkDataOffset,
                    out ChunkIndexEntry[] archiveEntries,
                    out long writeCursor,
                    out failureReason))
            {
                return false;
            }

            byte[] headerBlock = new byte[chunkDataOffset];
            AsavFormat.WriteAsavHeader(
                headerBlock.AsSpan(0, AsavFormat.AsavHeaderSize),
                sourceId.WorldGuid,
                checked((uint)snapByteLen),
                chunkDataOffset);

            SnapshotFormat.WriteSnapshotImageCore(
                headerBlock.AsSpan(AsavFormat.AsavHeaderSize, snapByteLen),
                sourceId.WorldGuid,
                raw.MetaBytes.Span,
                raw.ThumbBytes.Span,
                compCoreSpan,
                raw.Header.CoreUncompressedLength,
                coreCodec,
                archiveEntries);

            outputHandle.WriteAt(0, headerBlock);

            if (!SnapshotFormat.TryComputeStreamingXxHash64(outputHandle, writeCursor, out ulong wholeHash, out failureReason))
                return false;

            Span<byte> trailer = stackalloc byte[SnapshotFormat.WholeFileTrailerSize];
            BinaryPrimitives.WriteUInt64LittleEndian(trailer, wholeHash);
            outputHandle.WriteAt(writeCursor, trailer);

            if (flushToDisk)
                outputHandle.FlushToDisk();

            failureReason = null;
            return true;
        }
        finally
        {
            if (transcodedCoreBuf is not null)
                ArrayPool<byte>.Shared.Return(transcodedCoreBuf);
        }
    }

    /// <summary>
    /// Verifies and imports an external <c>.asav</c> archive file on disk into <paramref name="targetSegmentStore"/> and <paramref name="snapshotStore"/>.
    /// </summary>
    /// <typeparam name="TMeta">The snapshot metadata type.</typeparam>
    /// <param name="sourceFilePath">The path to the <c>.asav</c> file.</param>
    /// <param name="snapshotStore">The destination snapshot store.</param>
    /// <param name="targetSegmentStore">The destination segment store defining the target world.</param>
    /// <param name="targetSlotId">The destination snapshot slot identifier.</param>
    /// <param name="codecs">The shared codec set used for decompression.</param>
    /// <param name="imported">The imported snapshot metadata summary on success.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <param name="rewriteMeta">Optional transform applied to metadata before saving the imported snapshot.</param>
    /// <param name="flushToDisk">Whether to flush imported segments and snapshot to durable storage.</param>
    /// <returns><see langword="true"/> if verified and imported cleanly; otherwise, <see langword="false"/>.</returns>
    public static bool TryImportFromFile<TMeta>(
        string sourceFilePath,
        SnapshotStore<TMeta> snapshotStore,
        ChunkSegmentStore targetSegmentStore,
        string targetSlotId,
        ChunkCodecSet codecs,
        out SnapshotMetaInfo<TMeta> imported,
        [NotNullWhen(false)] out string? failureReason,
        Func<TMeta, TMeta>? rewriteMeta = null,
        bool flushToDisk = true) where TMeta : ISerializable, new()
    {
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(targetSegmentStore);
        ArgumentNullException.ThrowIfNull(codecs);
        StreamingSavePaths.ValidateSlotId(targetSlotId);

        if (!DiskFileUtility.TryOpenRead(sourceFilePath, out IRandomAccessHandle? handle))
        {
            imported = default;
            failureReason = $"Archive file '{sourceFilePath}' could not be opened.";
            return false;
        }

        using (handle)
        {
            return TryImport(
                handle,
                snapshotStore,
                targetSegmentStore,
                targetSlotId,
                codecs,
                out imported,
                out failureReason,
                rewriteMeta,
                flushToDisk);
        }
    }

    /// <summary>
    /// Verifies and imports an <c>.asav</c> archive from an <see cref="IRandomAccessHandle"/> into <paramref name="targetSegmentStore"/> and <paramref name="snapshotStore"/>.
    /// </summary>
    /// <typeparam name="TMeta">The snapshot metadata type.</typeparam>
    /// <param name="sourceHandle">The readable archive handle.</param>
    /// <param name="snapshotStore">The destination snapshot store.</param>
    /// <param name="targetSegmentStore">The destination segment store defining the target world.</param>
    /// <param name="targetSlotId">The destination snapshot slot identifier.</param>
    /// <param name="codecs">The shared codec set used for decompression.</param>
    /// <param name="imported">The imported snapshot metadata summary on success.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <param name="rewriteMeta">Optional transform applied to metadata before saving the imported snapshot.</param>
    /// <param name="flushToDisk">Whether to flush imported segments and snapshot to durable storage.</param>
    /// <returns><see langword="true"/> if verified and imported cleanly; otherwise, <see langword="false"/>.</returns>
    public static bool TryImport<TMeta>(
        IRandomAccessHandle sourceHandle,
        SnapshotStore<TMeta> snapshotStore,
        ChunkSegmentStore targetSegmentStore,
        string targetSlotId,
        ChunkCodecSet codecs,
        out SnapshotMetaInfo<TMeta> imported,
        [NotNullWhen(false)] out string? failureReason,
        Func<TMeta, TMeta>? rewriteMeta = null,
        bool flushToDisk = true) where TMeta : ISerializable, new()
    {
        ArgumentNullException.ThrowIfNull(sourceHandle);
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(targetSegmentStore);
        ArgumentNullException.ThrowIfNull(codecs);
        StreamingSavePaths.ValidateSlotId(targetSlotId);

        imported = default;
        if (!AsavArchiveReader<TMeta>.TryOpenHandle(
                sourceHandle,
                ownsHandle: false,
                codecs,
                out AsavArchiveReader<TMeta>? reader,
                out failureReason,
                verifyWholeFileHash: true,
                snapshotStore.MaxUncompressedCoreBytes))
        {
            return false;
        }

        using (reader)
        {
            IReadOnlyList<ChunkIndexEntry> archiveIndex = reader.ChunkIndex;
            ChunkIndexEntry[] committedIndex;

            if (archiveIndex.Count == 0)
            {
                committedIndex = [];
            }
            else
            {
                // Verify all archive chunks into contiguous slabs before creating any segment file.
                var slabs = new List<ArrayBufferWriter<byte>>();
                var currentSlab = new ArrayBufferWriter<byte>(64 * 1024);
                slabs.Add(currentSlab);
                var locations = new (int SlabIndex, int Start, int Length)[archiveIndex.Count];

                for (int i = 0; i < archiveIndex.Count; i++)
                {
                    ChunkIndexEntry entry = archiveIndex[i];
                    if (currentSlab.WrittenCount >= MaxImportSlabBytes)
                    {
                        int hint = Math.Max(64 * 1024, (int)Math.Min(entry.CompressedSize, int.MaxValue));
                        currentSlab = new ArrayBufferWriter<byte>(hint);
                        slabs.Add(currentSlab);
                    }

                    int start = currentSlab.WrittenCount;
                    if (!reader.TryReadCompressedChunk(entry, currentSlab, out failureReason))
                        return false;

                    locations[i] = (slabs.Count - 1, start, currentSlab.WrittenCount - start);
                }

                var batch = new PrecompressedChunkWrite[archiveIndex.Count];
                for (int i = 0; i < archiveIndex.Count; i++)
                {
                    ChunkIndexEntry entry = archiveIndex[i];
                    (int slabIdx, int start, int length) = locations[i];
                    batch[i] = new PrecompressedChunkWrite(
                        entry.Key,
                        slabs[slabIdx].WrittenMemory.Slice(start, length),
                        entry.UncompressedSize,
                        entry.Codec,
                        entry.Flags,
                        entry.SchemaVersion);
                }

                committedIndex = targetSegmentStore.AppendPrecompressedBatch(batch, flushToDisk);
            }

            TMeta finalMeta = rewriteMeta is not null ? rewriteMeta(reader.Meta) : reader.Meta;
            var targetId = new SnapshotId(targetSegmentStore.WorldGuid, targetSlotId);
            snapshotStore.WriteSnapshot(
                targetId,
                finalMeta,
                reader.CoreBytes.Span,
                committedIndex,
                reader.ThumbnailBytes.Span,
                reader.CoreCodec,
                flushToDisk);

            imported = new SnapshotMetaInfo<TMeta>(targetId, finalMeta, reader.HasThumbnail);
            failureReason = null;
            return true;
        }
    }

    private static bool TryTranscodeCore(
        ReadOnlySpan<byte> sourceCompressed,
        ChunkCodecId sourceCodec,
        int uncompressedLength,
        int maxUncompressedCoreBytes,
        ChunkCodecId targetCodec,
        ChunkCodecSet codecs,
        [NotNullWhen(true)] out byte[]? compressedBuffer,
        out int compressedLength,
        [NotNullWhen(false)] out string? failureReason)
    {
        compressedBuffer = null;
        compressedLength = 0;

        if ((uint)uncompressedLength > (uint)maxUncompressedCoreBytes)
        {
            failureReason = $"Snapshot core size {uncompressedLength} exceeds limit {maxUncompressedCoreBytes}.";
            return false;
        }

        byte[]? uncompressedBuf = uncompressedLength > 0 ? ArrayPool<byte>.Shared.Rent(uncompressedLength) : null;
        try
        {
            Span<byte> uncompressedSpan = uncompressedBuf is not null ? uncompressedBuf.AsSpan(0, uncompressedLength) : Span<byte>.Empty;
            if (!codecs.TryDecompress(sourceCodec, sourceCompressed, uncompressedSpan, out int written) || written != uncompressedLength)
            {
                failureReason = $"Failed to decompress snapshot core payload with codec {sourceCodec}.";
                return false;
            }

            int maxCoreComp = codecs.GetMaxCompressedLength(targetCodec, uncompressedLength);
            compressedBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, maxCoreComp));
            compressedLength = codecs.Compress(targetCodec, uncompressedSpan, compressedBuffer.AsSpan(0, maxCoreComp));
            failureReason = null;
            return true;
        }
        finally
        {
            if (uncompressedBuf is not null)
                ArrayPool<byte>.Shared.Return(uncompressedBuf);
        }
    }

    private static bool TryWriteArchiveChunks(
        ChunkSegmentStore segmentStore,
        ChunkIndexEntry[] sourceEntries,
        ChunkCodecId chunkCodec,
        ChunkCodecSet codecs,
        IRandomAccessHandle outputHandle,
        uint chunkDataOffset,
        out ChunkIndexEntry[] archiveEntries,
        out long writeCursor,
        [NotNullWhen(false)] out string? failureReason)
    {
        archiveEntries = sourceEntries.Length == 0 ? [] : new ChunkIndexEntry[sourceEntries.Length];
        writeCursor = chunkDataOffset;

        var uncompressedScratch = new ArrayBufferWriter<byte>(64 * 1024);
        var compressedScratch = new ArrayBufferWriter<byte>(64 * 1024);

        for (int i = 0; i < sourceEntries.Length; i++)
        {
            ChunkIndexEntry srcEntry = sourceEntries[i];
            compressedScratch.Clear();
            if (!ChunkRecordCodec.TryReadForTargetCodec(
                    segmentStore,
                    srcEntry,
                    chunkCodec,
                    codecs,
                    uncompressedScratch,
                    compressedScratch,
                    out failureReason))
            {
                return false;
            }

            ReadOnlySpan<byte> compressedPayload = compressedScratch.WrittenSpan;
            uint relativeOffset = checked((uint)(writeCursor - chunkDataOffset));
            ChunkIndexEntry archiveEntry = srcEntry.WithLocation(0, relativeOffset, checked((uint)compressedPayload.Length), chunkCodec);
            archiveEntries[i] = archiveEntry;

            int alignedLen = checked((int)ChunkRecordCodec.AlignedChunkRecordLength((uint)compressedPayload.Length));
            byte[] recordBuf = ArrayPool<byte>.Shared.Rent(alignedLen);
            try
            {
                int written = ChunkRecordCodec.WriteChunkRecord(recordBuf.AsSpan(0, alignedLen), archiveEntry, compressedPayload);
                outputHandle.WriteAt(writeCursor, recordBuf.AsSpan(0, written));
                writeCursor += written;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(recordBuf);
            }
        }

        failureReason = null;
        return true;
    }
}

internal static class AsavFormat
{
    public const int AsavHeaderSize = 32;
    public const ushort AsavFormatVersion = 1;

    public static ReadOnlySpan<byte> AsavMagicBytes => "ASAV"u8;

    public static void WriteAsavHeader(
        Span<byte> destination,
        Guid worldGuid,
        uint snapshotByteLength,
        uint chunkDataOffset)
    {
        AsavMagicBytes.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), AsavFormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), 0);
        ChunkRecordCodec.WriteGuidRfc4122(destination.Slice(8, 16), worldGuid);
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

        if (headerBytes.Length < AsavHeaderSize || totalFileLength < AsavHeaderSize + SnapshotFormat.WholeFileTrailerSize)
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

        worldGuid = ChunkRecordCodec.ReadGuidRfc4122(headerBytes.Slice(8, 16));
        snapshotByteLength = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(24, 4));
        chunkDataOffset = BinaryPrimitives.ReadUInt32LittleEndian(headerBytes.Slice(28, 4));

        long expectedChunkDataOffset = ChunkRecordCodec.Align16((long)AsavHeaderSize + snapshotByteLength);
        if (chunkDataOffset != expectedChunkDataOffset ||
            chunkDataOffset > totalFileLength - SnapshotFormat.WholeFileTrailerSize)
        {
            failureReason = "ASAV snapshot length or chunk data offset is out of bounds.";
            return false;
        }

        failureReason = null;
        return true;
    }
}
