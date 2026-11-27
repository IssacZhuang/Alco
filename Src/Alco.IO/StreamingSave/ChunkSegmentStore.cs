using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Alco.IO;

/// <summary>
/// Input chunk payload passed to <see cref="ChunkSegmentStore.AppendBatch"/>.
/// </summary>
/// <param name="Key">The chunk key.</param>
/// <param name="UncompressedPayload">The uncompressed chunk payload bytes.</param>
/// <param name="Flags">Optional chunk hint flags.</param>
/// <param name="SchemaVersion">The chunk payload schema version.</param>
public readonly record struct ChunkWriteInput(
    ChunkKey Key,
    ReadOnlyMemory<byte> UncompressedPayload,
    byte Flags = 0,
    byte SchemaVersion = ChunkRecordCodec.DefaultChunkSchemaVersion);

/// <summary>
/// Pre-compressed chunk payload passed to <see cref="ChunkSegmentStore.AppendPrecompressedBatch"/>.
/// </summary>
/// <param name="Key">The chunk key.</param>
/// <param name="CompressedPayload">The already-compressed payload bytes.</param>
/// <param name="UncompressedSize">The uncompressed payload size in bytes.</param>
/// <param name="Codec">The codec used to compress <paramref name="CompressedPayload"/>.</param>
/// <param name="Flags">Optional chunk hint flags.</param>
/// <param name="SchemaVersion">The chunk payload schema version.</param>
public readonly record struct PrecompressedChunkWrite(
    ChunkKey Key,
    ReadOnlyMemory<byte> CompressedPayload,
    uint UncompressedSize,
    ChunkCodecId Codec,
    byte Flags = 0,
    byte SchemaVersion = ChunkRecordCodec.DefaultChunkSchemaVersion);

/// <summary>
/// Configuration parameters governing segment compaction in <see cref="ChunkSegmentStore.Compact"/>.
/// </summary>
public sealed class CompactionOptions
{
    /// <summary>
    /// Gets or sets the dead-byte fraction (<c>0..1</c>) above which a segment is rewritten.
    /// </summary>
    public double DeadRatioThreshold { get; set; } = 0.40;

    /// <summary>
    /// Gets or sets the maximum number of non-empty segments tolerated before merging smaller segments.
    /// </summary>
    public int MaxSegmentCount { get; set; } = 16;

    /// <summary>
    /// Gets or sets the target compression codec used when rewriting live chunks during compaction.
    /// </summary>
    public ChunkCodecId TargetCodec { get; set; } = ChunkCodecId.Zstd;

    /// <summary>
    /// Gets or sets whether to rewrite all non-empty segments regardless of dead-byte ratio.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>
    /// Gets or sets whether to flush newly created compacted segments to durable storage before updating references.
    /// </summary>
    public bool FlushToDisk { get; set; } = true;
}

/// <summary>
/// Summary of a <see cref="ChunkSegmentStore.Compact"/> invocation.
/// </summary>
/// <param name="Executed">Whether any dead segments were removed or live chunks rewritten.</param>
/// <param name="RewrittenChunks">The number of live chunk records copied or transcoded to new segments.</param>
/// <param name="RemovedSegments">The number of obsolete segments retired and deleted.</param>
/// <param name="CreatedSegments">The number of new compacted segments created.</param>
/// <param name="BytesReclaimed">The net disk bytes reclaimed (negative if transcoding grew payloads).</param>
public readonly record struct CompactionReport(
    bool Executed,
    int RewrittenChunks,
    int RemovedSegments,
    int CreatedSegments,
    long BytesReclaimed);

/// <summary>
/// Manages immutable append-only chunk segments (<c>seg_NNNNNN.chk</c>) for a single world with concurrent reading and crash-safe compaction.
/// </summary>
public sealed class ChunkSegmentStore : IChunkReader, IDisposable
{
    private readonly IRandomAccessStorage _storage;
    private readonly Guid _worldGuid;
    private readonly ChunkCodecSet _codecs;
    private readonly SegmentHandleCache _handleCache;
    private readonly object _writeLock = new();

    private uint _nextSegmentId;
    private IRandomAccessHandle? _activeWriteHandle;
    private uint _activeWriteSegmentId;
    private long _activeWriteCursor;
    private SegmentFlags _activeWriteFlags;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a segment store for <paramref name="worldGuid"/> over <paramref name="storage"/>.
    /// </summary>
    /// <param name="storage">The backing storage.</param>
    /// <param name="worldGuid">The world identifier.</param>
    /// <param name="codecs">The shared codec set used for compression and decompression.</param>
    public ChunkSegmentStore(IRandomAccessStorage storage, Guid worldGuid, ChunkCodecSet codecs)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(codecs);
        _storage = storage;
        _worldGuid = worldGuid;
        _codecs = codecs;
        _handleCache = new SegmentHandleCache(storage, worldGuid);

        uint maxSegmentId = 0;
        IReadOnlyList<string> existingFiles = _storage.EnumerateFiles(
            StreamingSavePaths.ChunksDirectory(_worldGuid),
            StreamingSavePaths.SegmentFilePattern,
            recursive: false);
        for (int i = 0; i < existingFiles.Count; i++)
        {
            if (StreamingSavePaths.TryParseSegmentId(existingFiles[i], out uint segId) && segId > maxSegmentId)
                maxSegmentId = segId;
        }

        _nextSegmentId = maxSegmentId + 1;
    }

    /// <summary>Gets the world identifier managed by this store.</summary>
    public Guid WorldGuid => _worldGuid;

    /// <summary>Gets or sets the soft maximum byte size of a single segment file before rolling.</summary>
    public long MaxSegmentBytes { get; set; } = ChunkRecordCodec.DefaultMaxSegmentBytes;

    /// <summary>Gets or sets the maximum allowed uncompressed chunk size when reading records.</summary>
    public int MaxUncompressedChunkBytes { get; set; } = ChunkRecordCodec.DefaultMaxUncompressedChunkBytes;

    /// <inheritdoc/>
    public bool TryReadChunk(
        in ChunkIndexEntry entry,
        IBufferWriter<byte> uncompressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(uncompressedDestination);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (entry.SegmentId == 0)
        {
            failureReason = $"Invalid committed segment ID 0 for chunk {entry.Key}.";
            return false;
        }

        if (!_handleCache.TryAcquire(entry.SegmentId, out SegmentHandleCache.Lease lease, out failureReason))
            return false;

        using (lease)
        {
            return ChunkRecordCodec.TryReadAndDecompressChunkRecord(
                lease.Handle,
                entry.RecordOffset,
                entry,
                _codecs,
                MaxUncompressedChunkBytes,
                uncompressedDestination,
                out failureReason);
        }
    }

    /// <inheritdoc/>
    public bool TryReadCompressedChunk(
        in ChunkIndexEntry entry,
        IBufferWriter<byte> compressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(compressedDestination);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (entry.SegmentId == 0)
        {
            failureReason = $"Invalid committed segment ID 0 for chunk {entry.Key}.";
            return false;
        }

        if (!_handleCache.TryAcquire(entry.SegmentId, out SegmentHandleCache.Lease lease, out failureReason))
            return false;

        using (lease)
        {
            return ChunkRecordCodec.TryReadVerifiedCompressedRecord(
                lease.Handle,
                entry.RecordOffset,
                entry,
                compressedDestination,
                out failureReason);
        }
    }

    /// <summary>
    /// Compresses and appends a batch of chunks in Morton Z-order, rolling segments when <see cref="MaxSegmentBytes"/> is reached.
    /// </summary>
    /// <param name="chunks">The chunks to persist.</param>
    /// <param name="codec">The compression codec to apply.</param>
    /// <param name="flushToDisk">Whether to flush touched segments to durable storage before returning.</param>
    /// <returns>The resulting <see cref="ChunkIndexEntry"/> array sorted in <see cref="ChunkKey"/> order.</returns>
    public ChunkIndexEntry[] AppendBatch(
        ReadOnlySpan<ChunkWriteInput> chunks,
        ChunkCodecId codec = ChunkCodecId.Lz4,
        bool flushToDisk = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!ChunkCodecSet.IsValid(codec))
            throw new ArgumentOutOfRangeException(nameof(codec), $"Unsupported chunk codec {(byte)codec}.");
        if (chunks.IsEmpty)
            return [];

        var keys = new ChunkKey[chunks.Length];
        for (int i = 0; i < chunks.Length; i++)
            keys[i] = chunks[i].Key;

        int[] order = SortUniqueIndicesByKey(keys);
        var results = new ChunkIndexEntry[chunks.Length];

        lock (_writeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                for (int i = 0; i < order.Length; i++)
                {
                    ChunkWriteInput input = chunks[order[i]];
                    ReadOnlySpan<byte> uncompressed = input.UncompressedPayload.Span;
                    int maxCompressed = _codecs.GetMaxCompressedLength(codec, uncompressed.Length);
                    byte[]? compBuffer = maxCompressed > 0 ? ArrayPool<byte>.Shared.Rent(maxCompressed) : null;
                    try
                    {
                        Span<byte> compSpan = compBuffer is not null ? compBuffer.AsSpan(0, maxCompressed) : Span<byte>.Empty;
                        int compLength = _codecs.Compress(codec, uncompressed, compSpan);
                        results[i] = WriteRecordLocked(
                            input.Key,
                            compSpan[..compLength],
                            (uint)uncompressed.Length,
                            codec,
                            input.Flags,
                            input.SchemaVersion,
                            SegmentFlags.None,
                            flushToDisk);
                    }
                    finally
                    {
                        if (compBuffer is not null)
                            ArrayPool<byte>.Shared.Return(compBuffer);
                    }
                }

                if (flushToDisk)
                    _activeWriteHandle?.FlushToDisk();
            }
            catch
            {
                CloseActiveWriteHandleLocked(flush: false);
                throw;
            }
        }

        return results;
    }

    /// <summary>
    /// Appends pre-compressed chunk payloads in Morton Z-order without re-compressing them.
    /// </summary>
    /// <param name="chunks">The pre-compressed chunk inputs.</param>
    /// <param name="flushToDisk">Whether to flush touched segments to durable storage before returning.</param>
    /// <returns>The resulting <see cref="ChunkIndexEntry"/> array sorted in <see cref="ChunkKey"/> order.</returns>
    public ChunkIndexEntry[] AppendPrecompressedBatch(
        ReadOnlySpan<PrecompressedChunkWrite> chunks,
        bool flushToDisk = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (chunks.IsEmpty)
            return [];

        var keys = new ChunkKey[chunks.Length];
        for (int i = 0; i < chunks.Length; i++)
        {
            if (!ChunkCodecSet.IsValid(chunks[i].Codec))
                throw new ArgumentOutOfRangeException(nameof(chunks), $"Unsupported codec {(byte)chunks[i].Codec}.");

            keys[i] = chunks[i].Key;
        }

        int[] order = SortUniqueIndicesByKey(keys);
        var results = new ChunkIndexEntry[chunks.Length];

        lock (_writeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                for (int i = 0; i < order.Length; i++)
                {
                    PrecompressedChunkWrite input = chunks[order[i]];
                    results[i] = WriteRecordLocked(
                        input.Key,
                        input.CompressedPayload.Span,
                        input.UncompressedSize,
                        input.Codec,
                        input.Flags,
                        input.SchemaVersion,
                        SegmentFlags.None,
                        flushToDisk);
                }

                if (flushToDisk)
                    _activeWriteHandle?.FlushToDisk();
            }
            catch
            {
                CloseActiveWriteHandleLocked(flush: false);
                throw;
            }
        }

        return results;
    }

    /// <summary>
    /// Reclaims dead chunks and merges fragmented segments while preserving all distinct physical versions in <paramref name="liveEntries"/>.
    /// </summary>
    /// <param name="liveEntries">All live chunk index entries across every retained snapshot and active session baseline.</param>
    /// <param name="commitRelocations">Callback invoked after new compacted segments are flushed, before obsolete segments are deleted.</param>
    /// <param name="options">Optional compaction threshold and codec configuration.</param>
    /// <returns>A summary of the compaction run.</returns>
    public CompactionReport Compact(
        IEnumerable<ChunkIndexEntry> liveEntries,
        Action<IReadOnlyDictionary<ChunkRecordLocation, ChunkIndexEntry>> commitRelocations,
        CompactionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(liveEntries);
        ArgumentNullException.ThrowIfNull(commitRelocations);
        ObjectDisposedException.ThrowIf(_disposed, this);

        options ??= new CompactionOptions();
        if (!ChunkCodecSet.IsValid(options.TargetCodec))
            throw new ArgumentOutOfRangeException(nameof(options), $"Unsupported compaction target codec {(byte)options.TargetCodec}.");

        lock (_writeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CloseActiveWriteHandleLocked(flush: true);

            Dictionary<uint, (string Path, long Length)> segmentFiles = DiscoverSegmentsLocked();
            if (segmentFiles.Count == 0)
                return new CompactionReport(false, 0, 0, 0, 0);

            PlanCompaction(
                segmentFiles,
                liveEntries,
                options,
                out List<uint> deadSegmentIds,
                out HashSet<uint> rewriteSegmentSet,
                out List<ChunkIndexEntry> entriesToRewrite);

            if (deadSegmentIds.Count == 0 && rewriteSegmentSet.Count == 0)
                return new CompactionReport(false, 0, 0, 0, 0);

            RewriteSegmentsLocked(
                entriesToRewrite,
                options.TargetCodec,
                options.FlushToDisk,
                out Dictionary<ChunkRecordLocation, ChunkIndexEntry> relocations,
                out HashSet<uint> createdSegmentIds,
                out long newDataBytes);

            // Step 2: Atomic caller reference update BEFORE deleting any obsolete segment.
            commitRelocations(relocations);

            // Step 3: Retire and delete obsolete segments once no live snapshot references them.
            long removedBytes = RetireObsoleteSegments(segmentFiles, deadSegmentIds, rewriteSegmentSet);
            long createdHeaderBytes = (long)createdSegmentIds.Count * ChunkRecordCodec.SegmentHeaderSize;

            return new CompactionReport(
                Executed: true,
                RewrittenChunks: entriesToRewrite.Count,
                RemovedSegments: deadSegmentIds.Count + rewriteSegmentSet.Count,
                CreatedSegments: createdSegmentIds.Count,
                BytesReclaimed: removedBytes - (newDataBytes + createdHeaderBytes));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        lock (_writeLock)
        {
            CloseActiveWriteHandleLocked(flush: false);
        }

        _handleCache.Dispose();
    }

    private static void PlanCompaction(
        Dictionary<uint, (string Path, long Length)> segmentFiles,
        IEnumerable<ChunkIndexEntry> liveEntries,
        CompactionOptions options,
        out List<uint> deadSegmentIds,
        out HashSet<uint> rewriteSegmentSet,
        out List<ChunkIndexEntry> entriesToRewrite)
    {
        var uniqueLiveByLocation = new Dictionary<ChunkRecordLocation, ChunkIndexEntry>();
        var liveBytesPerSegment = new Dictionary<uint, long>();
        foreach (ChunkIndexEntry entry in liveEntries)
        {
            if (!segmentFiles.ContainsKey(entry.SegmentId))
                throw new InvalidDataException($"Live chunk {entry.Key} references missing segment {entry.SegmentId}.");

            ChunkRecordLocation location = entry.Location;
            if (uniqueLiveByLocation.TryGetValue(location, out ChunkIndexEntry existing))
            {
                if (existing != entry)
                    throw new InvalidDataException($"Conflicting live chunk index entries at segment {location.SegmentId} offset {location.RecordOffset}.");

                continue;
            }

            uniqueLiveByLocation.Add(location, entry);
            liveBytesPerSegment[entry.SegmentId] = liveBytesPerSegment.GetValueOrDefault(entry.SegmentId) +
                                                   ChunkRecordCodec.AlignedChunkRecordLength(entry.CompressedSize);
        }

        deadSegmentIds = [];
        var candidateSegments = new List<(uint SegmentId, double DeadRatio)>();
        foreach ((uint segId, (_, long fileLen)) in segmentFiles)
        {
            long payloadSpan = Math.Max(0L, fileLen - ChunkRecordCodec.SegmentHeaderSize);
            long liveBytes = liveBytesPerSegment.GetValueOrDefault(segId);
            if (liveBytes == 0)
            {
                deadSegmentIds.Add(segId);
                continue;
            }

            double deadRatio = payloadSpan > 0 ? Math.Max(0.0, (double)(payloadSpan - liveBytes) / payloadSpan) : 0.0;
            candidateSegments.Add((segId, deadRatio));
        }

        rewriteSegmentSet = SelectSegmentsToRewrite(candidateSegments, options);
        entriesToRewrite = [];
        if (rewriteSegmentSet.Count == 0)
            return;

        foreach (ChunkIndexEntry entry in uniqueLiveByLocation.Values)
        {
            if (rewriteSegmentSet.Contains(entry.SegmentId))
                entriesToRewrite.Add(entry);
        }

        entriesToRewrite.Sort(static (a, b) =>
        {
            int keyCmp = a.Key.CompareTo(b.Key);
            if (keyCmp != 0)
                return keyCmp;

            int segCmp = a.SegmentId.CompareTo(b.SegmentId);
            return segCmp != 0 ? segCmp : a.RecordOffset.CompareTo(b.RecordOffset);
        });
    }

    private void RewriteSegmentsLocked(
        List<ChunkIndexEntry> entriesToRewrite,
        ChunkCodecId targetCodec,
        bool flushToDisk,
        out Dictionary<ChunkRecordLocation, ChunkIndexEntry> relocations,
        out HashSet<uint> createdSegmentIds,
        out long newDataBytes)
    {
        relocations = new Dictionary<ChunkRecordLocation, ChunkIndexEntry>(entriesToRewrite.Count);
        createdSegmentIds = [];
        newDataBytes = 0;

        if (entriesToRewrite.Count == 0)
            return;

        var uncompressedScratch = new ArrayBufferWriter<byte>(64 * 1024);
        var compressedScratch = new ArrayBufferWriter<byte>(64 * 1024);
        try
        {
            for (int i = 0; i < entriesToRewrite.Count; i++)
            {
                ChunkIndexEntry oldEntry = entriesToRewrite[i];
                compressedScratch.Clear();
                if (!ChunkRecordCodec.TryReadForTargetCodec(
                        this,
                        oldEntry,
                        targetCodec,
                        _codecs,
                        uncompressedScratch,
                        compressedScratch,
                        out string? failureReason))
                {
                    throw new InvalidDataException($"Compaction failed processing chunk {oldEntry.Key}: {failureReason}");
                }

                ChunkIndexEntry newEntry = WriteRecordLocked(
                    oldEntry.Key,
                    compressedScratch.WrittenSpan,
                    oldEntry.UncompressedSize,
                    targetCodec,
                    oldEntry.Flags,
                    oldEntry.SchemaVersion,
                    SegmentFlags.Compacted,
                    flushToDisk);

                relocations.Add(oldEntry.Location, newEntry);
                createdSegmentIds.Add(newEntry.SegmentId);
                newDataBytes += ChunkRecordCodec.AlignedChunkRecordLength(newEntry.CompressedSize);
            }

            CloseActiveWriteHandleLocked(flush: flushToDisk);
        }
        catch
        {
            CloseActiveWriteHandleLocked(flush: false);
            foreach (uint createdId in createdSegmentIds)
                _handleCache.RetireAndDelete(createdId);
            throw;
        }
    }

    private long RetireObsoleteSegments(
        Dictionary<uint, (string Path, long Length)> segmentFiles,
        List<uint> deadSegmentIds,
        HashSet<uint> rewriteSegmentSet)
    {
        long removedBytes = 0;
        for (int i = 0; i < deadSegmentIds.Count; i++)
        {
            uint segId = deadSegmentIds[i];
            removedBytes += segmentFiles[segId].Length;
            _handleCache.RetireAndDelete(segId);
        }

        foreach (uint segId in rewriteSegmentSet)
        {
            removedBytes += segmentFiles[segId].Length;
            _handleCache.RetireAndDelete(segId);
        }

        return removedBytes;
    }

    private ChunkIndexEntry WriteRecordLocked(
        ChunkKey key,
        ReadOnlySpan<byte> compressedPayload,
        uint uncompressedSize,
        ChunkCodecId codec,
        byte flags,
        byte schemaVersion,
        SegmentFlags segmentFlags,
        bool flushOnRoll)
    {
        long alignedLengthLong = ChunkRecordCodec.AlignedChunkRecordLength((uint)compressedPayload.Length);
        if (alignedLengthLong > int.MaxValue)
            throw new IOException($"Chunk {key} record exceeds maximum single record length.");

        int alignedLength = (int)alignedLengthLong;
        long maxSegBytes = Math.Max(ChunkRecordCodec.SegmentHeaderSize + 1L, MaxSegmentBytes);

        bool mustRoll = _activeWriteHandle is null ||
                        _activeWriteFlags != segmentFlags ||
                        _activeWriteCursor > uint.MaxValue ||
                        (_activeWriteCursor > ChunkRecordCodec.SegmentHeaderSize && _activeWriteCursor + alignedLengthLong > maxSegBytes);

        if (mustRoll)
            RollWriteSegmentLocked(segmentFlags, flushOnRoll);

        uint offset = checked((uint)_activeWriteCursor);
        var entry = new ChunkIndexEntry(
            key.MapId,
            key.ChunkX,
            key.ChunkY,
            _activeWriteSegmentId,
            offset,
            checked((uint)compressedPayload.Length),
            uncompressedSize,
            codec,
            flags,
            schemaVersion);

        byte[] recordBuffer = ArrayPool<byte>.Shared.Rent(alignedLength);
        try
        {
            Span<byte> recordSpan = recordBuffer.AsSpan(0, alignedLength);
            int written = ChunkRecordCodec.WriteChunkRecord(recordSpan, entry, compressedPayload);
            _activeWriteHandle!.WriteAt(_activeWriteCursor, recordSpan[..written]);
            _activeWriteCursor += written;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(recordBuffer);
        }

        return entry;
    }

    private void RollWriteSegmentLocked(SegmentFlags segmentFlags, bool flushPrevious)
    {
        CloseActiveWriteHandleLocked(flushPrevious);

        uint segmentId = _nextSegmentId++;
        string relativePath = StreamingSavePaths.SegmentFilePath(_worldGuid, segmentId);
        IRandomAccessHandle handle = _storage.OpenAppendOrCreate(relativePath);
        try
        {
            Span<byte> header = stackalloc byte[ChunkRecordCodec.SegmentHeaderSize];
            ChunkRecordCodec.WriteSegmentHeader(header, segmentFlags, segmentId, _worldGuid, DateTime.UtcNow.Ticks);
            handle.WriteAt(0, header);
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        _activeWriteHandle = handle;
        _activeWriteSegmentId = segmentId;
        _activeWriteCursor = ChunkRecordCodec.SegmentHeaderSize;
        _activeWriteFlags = segmentFlags;
    }

    private void CloseActiveWriteHandleLocked(bool flush)
    {
        if (_activeWriteHandle is null)
            return;

        try
        {
            if (flush)
                _activeWriteHandle.FlushToDisk();
        }
        finally
        {
            _activeWriteHandle.Dispose();
            _activeWriteHandle = null;
            _activeWriteSegmentId = 0;
            _activeWriteCursor = 0;
            _activeWriteFlags = SegmentFlags.None;
        }
    }

    private Dictionary<uint, (string Path, long Length)> DiscoverSegmentsLocked()
    {
        IReadOnlyList<string> files = _storage.EnumerateFiles(
            StreamingSavePaths.ChunksDirectory(_worldGuid),
            StreamingSavePaths.SegmentFilePattern,
            recursive: false);
        var map = new Dictionary<uint, (string Path, long Length)>(files.Count);
        for (int i = 0; i < files.Count; i++)
        {
            string relPath = files[i];
            if (!StreamingSavePaths.TryParseSegmentId(relPath, out uint segId))
                continue;

            if (_storage.TryOpenRead(relPath, out IRandomAccessHandle? handle))
            {
                using (handle)
                {
                    map[segId] = (relPath, handle.Length);
                }
            }
        }

        return map;
    }

    private static HashSet<uint> SelectSegmentsToRewrite(
        List<(uint SegmentId, double DeadRatio)> candidates,
        CompactionOptions options)
    {
        var selected = new HashSet<uint>();
        if (candidates.Count == 0)
            return selected;

        if (options.Force)
        {
            for (int i = 0; i < candidates.Count; i++)
                selected.Add(candidates[i].SegmentId);

            return selected;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].DeadRatio > options.DeadRatioThreshold)
                selected.Add(candidates[i].SegmentId);
        }

        int maxSegments = Math.Max(1, options.MaxSegmentCount);
        int remainingUncompacted = candidates.Count - selected.Count;
        int resultingCount = remainingUncompacted + (selected.Count > 0 ? 1 : 0);
        if (resultingCount > maxSegments)
        {
            candidates.Sort(static (a, b) =>
            {
                int deadCmp = b.DeadRatio.CompareTo(a.DeadRatio);
                return deadCmp != 0 ? deadCmp : a.SegmentId.CompareTo(b.SegmentId);
            });

            int targetRemaining = Math.Max(0, maxSegments - 1);
            for (int i = 0; i < candidates.Count && remainingUncompacted > targetRemaining; i++)
            {
                if (selected.Add(candidates[i].SegmentId))
                    remainingUncompacted--;
            }
        }

        return selected;
    }

    private static int[] SortUniqueIndicesByKey(ChunkKey[] keys)
    {
        var indices = new int[keys.Length];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;

        Array.Sort(keys, indices);
        for (int i = 1; i < keys.Length; i++)
        {
            if (keys[i] == keys[i - 1])
                throw new ArgumentException($"Duplicate chunk key {keys[i]} in batch.");
        }

        return indices;
    }
}
