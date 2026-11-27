using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Alco.IO;

/// <summary>
/// Per-session exclusive temporary spill log (<c>spill_&lt;SessionGuid&gt;.tmp</c>) for evicted dirty chunks.
/// </summary>
public sealed class SpillStore : IDisposable
{
    private const int MaxDrainBatchBytes = 4 * 1024 * 1024;

    private readonly IRandomAccessStorage _storage;
    private readonly Guid _worldGuid;
    private readonly Guid _sessionGuid;
    private readonly ChunkCodecSet _codecs;
    private readonly ChunkCodecId _codecId;
    private readonly string _relativePath;
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly Dictionary<ChunkKey, ChunkIndexEntry> _latestEntries = new();

    private IRandomAccessHandle? _handle;
    private long _cursor;
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a session-scoped spill store.
    /// </summary>
    /// <param name="storage">The backing storage.</param>
    /// <param name="worldGuid">The world identifier.</param>
    /// <param name="sessionGuid">The unique session identifier.</param>
    /// <param name="codecs">The shared codec set used for compression and decompression.</param>
    /// <param name="codec">The compression codec used for spilled chunks (default <see cref="ChunkCodecId.Lz4"/>).</param>
    public SpillStore(
        IRandomAccessStorage storage,
        Guid worldGuid,
        Guid sessionGuid,
        ChunkCodecSet codecs,
        ChunkCodecId codec = ChunkCodecId.Lz4)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(codecs);
        if (!ChunkCodecSet.IsValid(codec))
            throw new ArgumentOutOfRangeException(nameof(codec), $"Unsupported chunk codec {(byte)codec}.");

        _storage = storage;
        _worldGuid = worldGuid;
        _sessionGuid = sessionGuid;
        _codecs = codecs;
        _codecId = codec;
        _relativePath = StreamingSavePaths.SpillFilePath(worldGuid, sessionGuid);
    }

    /// <summary>Gets the world identifier.</summary>
    public Guid WorldGuid => _worldGuid;

    /// <summary>Gets the unique session identifier.</summary>
    public Guid SessionGuid => _sessionGuid;

    /// <summary>Gets the compression codec used for spilled records.</summary>
    public ChunkCodecId Codec => _codecId;

    /// <summary>Gets or sets the maximum allowed uncompressed chunk size when reading spill records.</summary>
    public int MaxUncompressedChunkBytes { get; set; } = ChunkRecordCodec.DefaultMaxUncompressedChunkBytes;

    /// <summary>
    /// Gets the number of distinct chunks currently tracked in the spill log.
    /// </summary>
    public int SpilledChunkCount
    {
        get
        {
            _rwLock.EnterReadLock();
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _latestEntries.Count;
            }
            finally
            {
                _rwLock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// Compresses and appends a dirty chunk payload to the session spill file, updating its latest entry.
    /// </summary>
    /// <param name="key">The chunk key.</param>
    /// <param name="uncompressedPayload">The uncompressed chunk bytes.</param>
    /// <param name="flags">Optional chunk hint flags.</param>
    /// <param name="schemaVersion">The chunk payload schema version.</param>
    public void WriteChunk(
        ChunkKey key,
        ReadOnlySpan<byte> uncompressedPayload,
        byte flags = 0,
        byte schemaVersion = ChunkRecordCodec.DefaultChunkSchemaVersion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int maxCompressed = _codecs.GetMaxCompressedLength(_codecId, uncompressedPayload.Length);
        byte[]? compBuffer = maxCompressed > 0 ? ArrayPool<byte>.Shared.Rent(maxCompressed) : null;
        try
        {
            Span<byte> compSpan = compBuffer is not null ? compBuffer.AsSpan(0, maxCompressed) : Span<byte>.Empty;
            int compLength = _codecs.Compress(_codecId, uncompressedPayload, compSpan);
            long alignedLengthLong = ChunkRecordCodec.AlignedChunkRecordLength((uint)compLength);
            if (alignedLengthLong > int.MaxValue)
                throw new IOException($"Spill record for chunk {key} exceeds maximum length.");

            int alignedLength = (int)alignedLengthLong;
            byte[] recordBuffer = ArrayPool<byte>.Shared.Rent(alignedLength);
            try
            {
                _rwLock.EnterWriteLock();
                try
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    EnsureSpillHandleOpenLocked();

                    if (_cursor > uint.MaxValue)
                        throw new IOException("Session spill file exceeded 4 GiB offset limit.");

                    uint offset = (uint)_cursor;
                    var entry = new ChunkIndexEntry(
                        key.MapId,
                        key.ChunkX,
                        key.ChunkY,
                        segmentId: 0,
                        offset,
                        checked((uint)compLength),
                        checked((uint)uncompressedPayload.Length),
                        _codecId,
                        flags,
                        schemaVersion);

                    int written = ChunkRecordCodec.WriteChunkRecord(recordBuffer.AsSpan(0, alignedLength), entry, compSpan[..compLength]);
                    _handle!.WriteAt(_cursor, recordBuffer.AsSpan(0, written));
                    _cursor += written;
                    _latestEntries[key] = entry;
                }
                finally
                {
                    _rwLock.ExitWriteLock();
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(recordBuffer);
            }
        }
        finally
        {
            if (compBuffer is not null)
                ArrayPool<byte>.Shared.Return(compBuffer);
        }
    }

    /// <summary>
    /// Attempts to read and verify a spilled chunk for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The chunk key to look up.</param>
    /// <param name="uncompressedDestination">The buffer writer receiving the uncompressed chunk bytes.</param>
    /// <param name="entry">The matching spill entry when present.</param>
    /// <param name="failureReason">The corruption or I/O error reason when returning <see cref="ChunkReadStatus.Failed"/>.</param>
    /// <returns>The tri-state read outcome distinguishing missing entries from verification failures.</returns>
    public ChunkReadStatus TryReadChunk(
        ChunkKey key,
        IBufferWriter<byte> uncompressedDestination,
        out ChunkIndexEntry entry,
        out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(uncompressedDestination);

        _rwLock.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_latestEntries.TryGetValue(key, out entry))
            {
                failureReason = null;
                return ChunkReadStatus.NotFound;
            }

            if (_handle is null)
            {
                failureReason = $"Spill handle for session {_sessionGuid:N} is unavailable.";
                return ChunkReadStatus.Failed;
            }

            return ChunkRecordCodec.TryReadAndDecompressChunkRecord(
                _handle,
                entry.RecordOffset,
                entry,
                _codecs,
                MaxUncompressedChunkBytes,
                uncompressedDestination,
                out failureReason)
                ? ChunkReadStatus.Success
                : ChunkReadStatus.Failed;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Checks whether <paramref name="key"/> currently has a spilled record.
    /// </summary>
    /// <param name="key">The chunk key.</param>
    /// <returns><see langword="true"/> if spilled; otherwise, <see langword="false"/>.</returns>
    public bool Contains(ChunkKey key)
    {
        _rwLock.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _latestEntries.ContainsKey(key);
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Drops the spilled entry for <paramref name="key"/> if present.
    /// </summary>
    /// <param name="key">The chunk key.</param>
    /// <returns><see langword="true"/> if an entry was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(ChunkKey key)
    {
        _rwLock.EnterWriteLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _latestEntries.Remove(key);
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// Returns a snapshot of all chunk keys currently tracked in this spill log.
    /// </summary>
    /// <returns>The spilled chunk keys.</returns>
    public IReadOnlyList<ChunkKey> GetSpilledKeys()
    {
        _rwLock.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var keys = new ChunkKey[_latestEntries.Count];
            int idx = 0;
            foreach (ChunkKey key in _latestEntries.Keys)
                keys[idx++] = key;

            Array.Sort(keys);
            return keys;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Verifies and transfers spilled chunks (except <paramref name="excludeKeys"/>) into <paramref name="targetStore"/>
    /// in bounded memory batches without holding the spill write lock across target I/O.
    /// </summary>
    /// <param name="targetStore">The committed segment store receiving drained chunks.</param>
    /// <param name="targetCodec">Optional target codec; <see langword="null"/> preserves each spilled chunk's codec.</param>
    /// <param name="flushToDisk">Whether to flush target segments to durable storage.</param>
    /// <param name="excludeKeys">Optional keys superseded by newer in-memory state.</param>
    /// <returns>The committed <see cref="ChunkIndexEntry"/> array sorted in <see cref="ChunkKey"/> order.</returns>
    public ChunkIndexEntry[] DrainTo(
        ChunkSegmentStore targetStore,
        ChunkCodecId? targetCodec = null,
        bool flushToDisk = true,
        IReadOnlySet<ChunkKey>? excludeKeys = null)
    {
        ArgumentNullException.ThrowIfNull(targetStore);
        if (targetStore.WorldGuid != _worldGuid)
            throw new ArgumentException("Target segment store world GUID does not match spill store world GUID.", nameof(targetStore));
        if (targetCodec.HasValue && !ChunkCodecSet.IsValid(targetCodec.Value))
            throw new ArgumentOutOfRangeException(nameof(targetCodec), $"Unsupported chunk codec {(byte)targetCodec.Value}.");

        List<ChunkIndexEntry> toDrain = SnapshotDrainEntries(excludeKeys);
        if (toDrain.Count == 0)
            return [];

        toDrain.Sort(static (a, b) => a.Key.CompareTo(b.Key));

        var batchArena = new ArrayBufferWriter<byte>(64 * 1024);
        var uncompressedScratch = new ArrayBufferWriter<byte>(64 * 1024);
        var batchMeta = new List<(ChunkIndexEntry Entry, ChunkCodecId Codec, int Start, int Length)>();
        var committedAll = new List<ChunkIndexEntry>(toDrain.Count);
        var drainedEntries = new List<ChunkIndexEntry>(toDrain.Count);

        for (int i = 0; i < toDrain.Count; i++)
        {
            ChunkIndexEntry entry = toDrain[i];
            ChunkCodecId effectiveCodec = targetCodec ?? entry.Codec;
            int start = batchArena.WrittenCount;

            _rwLock.EnterReadLock();
            try
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_handle is null)
                    throw new InvalidOperationException("Spill entries exist without an open spill handle.");

                if (!ChunkRecordCodec.TryReadForTargetCodec(
                        _handle,
                        entry.RecordOffset,
                        entry,
                        effectiveCodec,
                        _codecs,
                        MaxUncompressedChunkBytes,
                        uncompressedScratch,
                        batchArena,
                        out string? failureReason))
                {
                    throw new InvalidDataException($"Spill record decode failed for {entry.Key}: {failureReason}");
                }
            }
            finally
            {
                _rwLock.ExitReadLock();
            }

            batchMeta.Add((entry, effectiveCodec, start, batchArena.WrittenCount - start));
            drainedEntries.Add(entry);

            bool isLast = i == toDrain.Count - 1;
            if (batchArena.WrittenCount >= MaxDrainBatchBytes || isLast)
            {
                ChunkIndexEntry[] batchCommitted = FlushDrainBatch(targetStore, batchArena, batchMeta, flushToDisk && isLast);
                committedAll.AddRange(batchCommitted);
                batchArena.Clear();
                batchMeta.Clear();
            }
        }

        CommitDrainedRemovals(drainedEntries);
        return committedAll.ToArray();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        bool alreadyDisposed;
        _rwLock.EnterWriteLock();
        try
        {
            alreadyDisposed = _disposed;
            if (!alreadyDisposed)
            {
                _disposed = true;
                _latestEntries.Clear();
                CloseAndDeleteSpillFileLocked();
            }
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }

        if (alreadyDisposed)
            return;

        _storage.DeleteDirectoryIfEmpty(StreamingSavePaths.SessionDirectory(_worldGuid));
        _rwLock.Dispose();
    }

    private List<ChunkIndexEntry> SnapshotDrainEntries(IReadOnlySet<ChunkKey>? excludeKeys)
    {
        _rwLock.EnterReadLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_latestEntries.Count == 0)
                return [];

            var result = new List<ChunkIndexEntry>(_latestEntries.Count);
            foreach ((ChunkKey key, ChunkIndexEntry entry) in _latestEntries)
            {
                if (excludeKeys is not null && excludeKeys.Contains(key))
                    continue;

                result.Add(entry);
            }

            return result;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    private static ChunkIndexEntry[] FlushDrainBatch(
        ChunkSegmentStore targetStore,
        ArrayBufferWriter<byte> batchArena,
        List<(ChunkIndexEntry Entry, ChunkCodecId Codec, int Start, int Length)> batchMeta,
        bool flushToDisk)
    {
        var batch = new PrecompressedChunkWrite[batchMeta.Count];
        ReadOnlyMemory<byte> arenaMemory = batchArena.WrittenMemory;
        for (int j = 0; j < batchMeta.Count; j++)
        {
            (ChunkIndexEntry item, ChunkCodecId codec, int start, int length) = batchMeta[j];
            batch[j] = new PrecompressedChunkWrite(
                item.Key,
                arenaMemory.Slice(start, length),
                item.UncompressedSize,
                codec,
                item.Flags,
                item.SchemaVersion);
        }

        return targetStore.AppendPrecompressedBatch(batch, flushToDisk);
    }

    private void CommitDrainedRemovals(List<ChunkIndexEntry> drainedEntries)
    {
        _rwLock.EnterWriteLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            for (int i = 0; i < drainedEntries.Count; i++)
            {
                ChunkIndexEntry drained = drainedEntries[i];
                if (_latestEntries.TryGetValue(drained.Key, out ChunkIndexEntry current) && current == drained)
                    _latestEntries.Remove(drained.Key);
            }

            if (_latestEntries.Count == 0)
                CloseAndDeleteSpillFileLocked();
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
    }

    private void EnsureSpillHandleOpenLocked()
    {
        if (_handle is not null)
            return;

        _storage.DeleteFile(_relativePath);
        IRandomAccessHandle handle = _storage.OpenAppendOrCreate(_relativePath);
        try
        {
            Span<byte> header = stackalloc byte[ChunkRecordCodec.SegmentHeaderSize];
            ChunkRecordCodec.WriteSegmentHeader(header, SegmentFlags.Spill, 0, _worldGuid, DateTime.UtcNow.Ticks);
            handle.WriteAt(0, header);
        }
        catch
        {
            handle.Dispose();
            _storage.DeleteFile(_relativePath);
            throw;
        }

        _handle = handle;
        _cursor = ChunkRecordCodec.SegmentHeaderSize;
    }

    private void CloseAndDeleteSpillFileLocked()
    {
        if (_handle is not null)
        {
            _handle.Dispose();
            _handle = null;
        }

        _cursor = 0;
        _storage.DeleteFile(_relativePath);
    }
}
