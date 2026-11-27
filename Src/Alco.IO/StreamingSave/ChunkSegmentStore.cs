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
    byte SchemaVersion = StreamingSaveFormat.DefaultChunkSchemaVersion);

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
    byte SchemaVersion = StreamingSaveFormat.DefaultChunkSchemaVersion);

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
public sealed class ChunkSegmentStore : IDisposable
{
    private readonly IRandomAccessStorage _storage;
    private readonly Guid _worldGuid;
    private readonly ChunkCodecSet _codecs;
    private readonly bool _ownsCodecs;
    private readonly object _writeLock = new();
    private readonly object _handleLock = new();
    private readonly Dictionary<uint, SegmentHandleRef> _readHandles = new();
    private readonly HashSet<uint> _retiredSegmentIds = [];

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
    /// <param name="codecs">Optional shared codec set; a dedicated set is created and owned when <see langword="null"/>.</param>
    public ChunkSegmentStore(IRandomAccessStorage storage, Guid worldGuid, ChunkCodecSet? codecs = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
        _worldGuid = worldGuid;
        _ownsCodecs = codecs is null;
        _codecs = codecs ?? new ChunkCodecSet();

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
    public long MaxSegmentBytes { get; set; } = StreamingSaveFormat.DefaultMaxSegmentBytes;

    /// <summary>Gets or sets the maximum allowed uncompressed chunk size when reading records.</summary>
    public int MaxUncompressedChunkBytes { get; set; } = StreamingSaveFormat.DefaultMaxUncompressedChunkBytes;

    /// <summary>
    /// Reads, verifies, and decompresses the chunk described by <paramref name="entry"/> in a thread-safe manner.
    /// </summary>
    /// <param name="entry">The chunk index entry locating the record.</param>
    /// <param name="uncompressedDestination">The buffer writer receiving the uncompressed chunk payload.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if the chunk passed CRC verification and decompressed cleanly; otherwise, <see langword="false"/>.</returns>
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

        if (!TryAcquireSegmentHandle(entry.SegmentId, out SegmentHandleRef? handleRef, out failureReason))
            return false;

        try
        {
            return StreamingSaveFormat.TryReadAndDecompressChunkRecord(
                handleRef.Handle,
                entry.RecordOffset,
                entry,
                _codecs,
                MaxUncompressedChunkBytes,
                uncompressedDestination,
                out failureReason);
        }
        finally
        {
            ReleaseSegmentHandle(handleRef);
        }
    }

    /// <summary>
    /// Reads and verifies the compressed chunk payload for <paramref name="entry"/> without decompressing it.
    /// </summary>
    /// <param name="entry">The chunk index entry locating the record.</param>
    /// <param name="compressedDestination">The buffer writer receiving the verified compressed payload.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if the record passed verification; otherwise, <see langword="false"/>.</returns>
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

        if (!TryAcquireSegmentHandle(entry.SegmentId, out SegmentHandleRef? handleRef, out failureReason))
            return false;

        try
        {
            return StreamingSaveFormat.TryReadVerifiedCompressedRecord(
                handleRef.Handle,
                entry.RecordOffset,
                entry,
                compressedDestination,
                out failureReason);
        }
        finally
        {
            ReleaseSegmentHandle(handleRef);
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
        if (chunks.IsEmpty)
            return [];

        IChunkCodec codecImpl = _codecs.GetCodec(codec);
        int[] order = SortIndicesByKey(chunks);
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
                    int maxCompressed = codecImpl.GetMaxCompressedLength(uncompressed.Length);
                    byte[]? compBuffer = maxCompressed > 0 ? ArrayPool<byte>.Shared.Rent(maxCompressed) : null;
                    try
                    {
                        Span<byte> compSpan = compBuffer is not null ? compBuffer.AsSpan(0, maxCompressed) : Span<byte>.Empty;
                        int compLength = codecImpl.Compress(uncompressed, compSpan);
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

        for (int i = 0; i < chunks.Length; i++)
        {
            if (!_codecs.TryGetCodec(chunks[i].Codec, out _))
                throw new ArgumentOutOfRangeException(nameof(chunks), $"Unsupported codec {(byte)chunks[i].Codec}.");
        }

        int[] order = SortPrecompressedIndicesByKey(chunks);
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
        IChunkCodec targetCodecImpl = _codecs.GetCodec(options.TargetCodec);

        lock (_writeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CloseActiveWriteHandleLocked(flush: true);

            Dictionary<uint, (string Path, long Length)> segmentFiles = DiscoverSegmentsLocked();
            if (segmentFiles.Count == 0)
                return new CompactionReport(false, 0, 0, 0, 0);

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
                                                       StreamingSaveFormat.AlignedChunkRecordLength(entry.CompressedSize);
            }

            var deadSegmentIds = new List<uint>();
            var candidateSegments = new List<(uint SegmentId, double DeadRatio)>();
            foreach ((uint segId, (_, long fileLen)) in segmentFiles)
            {
                long payloadSpan = Math.Max(0L, fileLen - StreamingSaveFormat.SegmentHeaderSize);
                long liveBytes = liveBytesPerSegment.GetValueOrDefault(segId);
                if (liveBytes == 0)
                {
                    deadSegmentIds.Add(segId);
                    continue;
                }

                double deadRatio = payloadSpan > 0 ? Math.Max(0.0, (double)(payloadSpan - liveBytes) / payloadSpan) : 0.0;
                candidateSegments.Add((segId, deadRatio));
            }

            var rewriteSegmentSet = SelectSegmentsToRewrite(candidateSegments, options);
            if (deadSegmentIds.Count == 0 && rewriteSegmentSet.Count == 0)
                return new CompactionReport(false, 0, 0, 0, 0);

            var entriesToRewrite = new List<ChunkIndexEntry>();
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

            var relocations = new Dictionary<ChunkRecordLocation, ChunkIndexEntry>(entriesToRewrite.Count);
            var createdSegmentIds = new HashSet<uint>();
            long newDataBytes = 0;

            if (entriesToRewrite.Count > 0)
            {
                var rawScratch = new ArrayBufferWriter<byte>(64 * 1024);
                try
                {
                    for (int i = 0; i < entriesToRewrite.Count; i++)
                    {
                        ChunkIndexEntry oldEntry = entriesToRewrite[i];
                        ChunkIndexEntry newEntry = RewriteSingleEntryLocked(
                            oldEntry,
                            targetCodecImpl,
                            rawScratch,
                            options.FlushToDisk);
                        relocations.Add(oldEntry.Location, newEntry);
                        createdSegmentIds.Add(newEntry.SegmentId);
                        newDataBytes += StreamingSaveFormat.AlignedChunkRecordLength(newEntry.CompressedSize);
                    }

                    CloseActiveWriteHandleLocked(flush: options.FlushToDisk);
                }
                catch
                {
                    CloseActiveWriteHandleLocked(flush: false);
                    foreach (uint createdId in createdSegmentIds)
                        RetireAndDeleteSegment(createdId);
                    throw;
                }
            }

            // Step 2: Atomic caller reference update BEFORE deleting any obsolete segment.
            commitRelocations(relocations);

            // Step 3: Retire and delete obsolete segments once no live snapshot references them.
            long removedBytes = 0;
            for (int i = 0; i < deadSegmentIds.Count; i++)
            {
                uint segId = deadSegmentIds[i];
                removedBytes += segmentFiles[segId].Length;
                RetireAndDeleteSegment(segId);
            }

            foreach (uint segId in rewriteSegmentSet)
            {
                removedBytes += segmentFiles[segId].Length;
                RetireAndDeleteSegment(segId);
            }

            long createdHeaderBytes = (long)createdSegmentIds.Count * StreamingSaveFormat.SegmentHeaderSize;
            long bytesReclaimed = removedBytes - (newDataBytes + createdHeaderBytes);
            return new CompactionReport(
                Executed: true,
                RewrittenChunks: entriesToRewrite.Count,
                RemovedSegments: deadSegmentIds.Count + rewriteSegmentSet.Count,
                CreatedSegments: createdSegmentIds.Count,
                BytesReclaimed: bytesReclaimed);
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

        List<IRandomAccessHandle>? toDispose = null;
        lock (_handleLock)
        {
            foreach (SegmentHandleRef handleRef in _readHandles.Values)
            {
                handleRef.Retired = true;
                handleRef.PendingDelete = false;
                handleRef.RefCount--;
                if (handleRef.RefCount == 0)
                    (toDispose ??= []).Add(handleRef.Handle);
            }

            _readHandles.Clear();
        }

        if (toDispose is not null)
        {
            for (int i = 0; i < toDispose.Count; i++)
                toDispose[i].Dispose();
        }

        if (_ownsCodecs)
            _codecs.Dispose();
    }

    private ChunkIndexEntry RewriteSingleEntryLocked(
        in ChunkIndexEntry oldEntry,
        IChunkCodec targetCodec,
        ArrayBufferWriter<byte> scratch,
        bool flushOnRoll)
    {
        scratch.Clear();
        if (oldEntry.Codec == targetCodec.Id)
        {
            if (!TryReadCompressedChunk(oldEntry, scratch, out string? failureReason))
                throw new InvalidDataException($"Compaction failed reading compressed chunk {oldEntry.Key}: {failureReason}");

            return WriteRecordLocked(
                oldEntry.Key,
                scratch.WrittenSpan,
                oldEntry.UncompressedSize,
                targetCodec.Id,
                oldEntry.Flags,
                oldEntry.SchemaVersion,
                SegmentFlags.Compacted,
                flushOnRoll);
        }

        if (!TryReadChunk(oldEntry, scratch, out string? readFailure))
            throw new InvalidDataException($"Compaction failed decoding chunk {oldEntry.Key}: {readFailure}");

        ReadOnlySpan<byte> uncompressed = scratch.WrittenSpan;
        int maxComp = targetCodec.GetMaxCompressedLength(uncompressed.Length);
        byte[]? compBuffer = maxComp > 0 ? ArrayPool<byte>.Shared.Rent(maxComp) : null;
        try
        {
            Span<byte> compSpan = compBuffer is not null ? compBuffer.AsSpan(0, maxComp) : Span<byte>.Empty;
            int compLength = targetCodec.Compress(uncompressed, compSpan);
            return WriteRecordLocked(
                oldEntry.Key,
                compSpan[..compLength],
                oldEntry.UncompressedSize,
                targetCodec.Id,
                oldEntry.Flags,
                oldEntry.SchemaVersion,
                SegmentFlags.Compacted,
                flushOnRoll);
        }
        finally
        {
            if (compBuffer is not null)
                ArrayPool<byte>.Shared.Return(compBuffer);
        }
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
        long alignedLengthLong = StreamingSaveFormat.AlignedChunkRecordLength((uint)compressedPayload.Length);
        if (alignedLengthLong > int.MaxValue)
            throw new IOException($"Chunk {key} record exceeds maximum single record length.");

        int alignedLength = (int)alignedLengthLong;
        long maxSegBytes = Math.Max(StreamingSaveFormat.SegmentHeaderSize + 1L, MaxSegmentBytes);

        bool mustRoll = _activeWriteHandle is null ||
                        _activeWriteFlags != segmentFlags ||
                        _activeWriteCursor > uint.MaxValue ||
                        (_activeWriteCursor > StreamingSaveFormat.SegmentHeaderSize && _activeWriteCursor + alignedLengthLong > maxSegBytes);

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
            int written = StreamingSaveFormat.WriteChunkRecord(recordSpan, entry, compressedPayload);
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
            Span<byte> header = stackalloc byte[StreamingSaveFormat.SegmentHeaderSize];
            StreamingSaveFormat.WriteSegmentHeader(header, segmentFlags, segmentId, _worldGuid, DateTime.UtcNow.Ticks);
            handle.WriteAt(0, header);
        }
        catch
        {
            handle.Dispose();
            throw;
        }

        _activeWriteHandle = handle;
        _activeWriteSegmentId = segmentId;
        _activeWriteCursor = StreamingSaveFormat.SegmentHeaderSize;
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

    private bool TryAcquireSegmentHandle(
        uint segmentId,
        [NotNullWhen(true)] out SegmentHandleRef? handleRef,
        [NotNullWhen(false)] out string? failureReason)
    {
        lock (_handleLock)
        {
            if (_retiredSegmentIds.Contains(segmentId))
            {
                handleRef = null;
                failureReason = $"Segment {segmentId} has been retired.";
                return false;
            }

            if (_readHandles.TryGetValue(segmentId, out SegmentHandleRef? cached) && !cached.Retired)
            {
                cached.RefCount++;
                handleRef = cached;
                failureReason = null;
                return true;
            }
        }

        string relativePath = StreamingSavePaths.SegmentFilePath(_worldGuid, segmentId);
        if (!_storage.TryOpenRead(relativePath, out IRandomAccessHandle? openedHandle))
        {
            handleRef = null;
            failureReason = $"Segment file '{relativePath}' does not exist.";
            return false;
        }

        if (!StreamingSaveFormat.TryReadAndValidateSegmentHeader(
                openedHandle,
                _worldGuid,
                segmentId,
                expectedSpill: false,
                out _,
                out failureReason))
        {
            openedHandle.Dispose();
            handleRef = null;
            return false;
        }

        var newRef = new SegmentHandleRef(openedHandle, relativePath);
        bool keepNewRef = false;
        lock (_handleLock)
        {
            if (_disposed)
            {
                handleRef = null;
                failureReason = "Segment store was disposed.";
                openedHandle.Dispose();
                return false;
            }

            if (_retiredSegmentIds.Contains(segmentId))
            {
                // Segment was retired while we opened it; let this in-flight read finish without caching it.
                newRef.Retired = true;
                newRef.PendingDelete = false;
                newRef.RefCount = 1;
                handleRef = newRef;
                keepNewRef = true;
            }
            else if (_readHandles.TryGetValue(segmentId, out SegmentHandleRef? existing) && !existing.Retired)
            {
                existing.RefCount++;
                handleRef = existing;
            }
            else
            {
                newRef.RefCount = 2; // 1 for cache + 1 for caller.
                _readHandles[segmentId] = newRef;
                handleRef = newRef;
                keepNewRef = true;
            }
        }

        if (!keepNewRef)
            openedHandle.Dispose();

        failureReason = null;
        return true;
    }

    private void ReleaseSegmentHandle(SegmentHandleRef handleRef)
    {
        IRandomAccessHandle? toDispose = null;
        string? pathToDelete = null;
        lock (_handleLock)
        {
            handleRef.RefCount--;
            if (handleRef.Retired && handleRef.RefCount == 0)
            {
                toDispose = handleRef.Handle;
                if (handleRef.PendingDelete)
                    pathToDelete = handleRef.RelativePath;
            }
        }

        toDispose?.Dispose();
        if (pathToDelete is not null)
            _storage.DeleteFile(pathToDelete);
    }

    private void RetireAndDeleteSegment(uint segmentId)
    {
        IRandomAccessHandle? toDispose = null;
        string? pathToDelete = null;
        bool hadCachedHandle = false;

        lock (_handleLock)
        {
            _retiredSegmentIds.Add(segmentId);
            if (_readHandles.Remove(segmentId, out SegmentHandleRef? cached))
            {
                hadCachedHandle = true;
                cached.Retired = true;
                cached.PendingDelete = true;
                cached.RefCount--;
                if (cached.RefCount == 0)
                {
                    toDispose = cached.Handle;
                    pathToDelete = cached.RelativePath;
                }
            }
        }

        toDispose?.Dispose();
        if (pathToDelete is not null)
        {
            _storage.DeleteFile(pathToDelete);
        }
        else if (!hadCachedHandle)
        {
            _storage.DeleteFile(StreamingSavePaths.SegmentFilePath(_worldGuid, segmentId));
        }
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

    private static int[] SortIndicesByKey(ReadOnlySpan<ChunkWriteInput> chunks)
    {
        int[] indices = new int[chunks.Length];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;

        var keys = new ChunkKey[chunks.Length];
        for (int i = 0; i < chunks.Length; i++)
            keys[i] = chunks[i].Key;

        Array.Sort(keys, indices);
        for (int i = 1; i < keys.Length; i++)
        {
            if (keys[i] == keys[i - 1])
                throw new ArgumentException($"Duplicate chunk key {keys[i]} in batch.", nameof(chunks));
        }

        return indices;
    }

    private static int[] SortPrecompressedIndicesByKey(ReadOnlySpan<PrecompressedChunkWrite> chunks)
    {
        int[] indices = new int[chunks.Length];
        for (int i = 0; i < indices.Length; i++)
            indices[i] = i;

        var keys = new ChunkKey[chunks.Length];
        for (int i = 0; i < chunks.Length; i++)
            keys[i] = chunks[i].Key;

        Array.Sort(keys, indices);
        for (int i = 1; i < keys.Length; i++)
        {
            if (keys[i] == keys[i - 1])
                throw new ArgumentException($"Duplicate chunk key {keys[i]} in batch.", nameof(chunks));
        }

        return indices;
    }

    private sealed class SegmentHandleRef(IRandomAccessHandle handle, string relativePath)
    {
        public IRandomAccessHandle Handle { get; } = handle;
        public string RelativePath { get; } = relativePath;
        public int RefCount;
        public bool Retired;
        public bool PendingDelete;
    }
}
