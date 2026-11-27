using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace Alco.IO;

/// <summary>
/// Result status returned by <see cref="SpillStore.TryReadChunk"/>.
/// </summary>
public enum SpillReadStatus : byte
{
    /// <summary>The requested chunk has not been spilled during this session.</summary>
    NotFound = 0,

    /// <summary>The spilled chunk was verified and decompressed into the destination buffer.</summary>
    Success = 1,

    /// <summary>The chunk was present in the spill log, but reading or verifying it failed.</summary>
    Failed = 2
}

/// <summary>
/// Per-session exclusive temporary spill log (<c>spill_&lt;SessionGuid&gt;.tmp</c>) for evicted dirty chunks.
/// </summary>
public sealed class SpillStore : IDisposable
{
    private readonly IRandomAccessStorage _storage;
    private readonly Guid _worldGuid;
    private readonly Guid _sessionGuid;
    private readonly ChunkCodecId _codecId;
    private readonly ChunkCodecSet _codecs;
    private readonly bool _ownsCodecs;
    private readonly string _relativePath;
    private readonly ReaderWriterLockSlim _rwLock = new();
    private readonly Dictionary<ChunkKey, ChunkIndexEntry> _latestEntries = new();

    private IRandomAccessHandle? _handle;
    private long _cursor;
    private bool _disposed;

    /// <summary>
    /// Initializes a session-scoped spill store.
    /// </summary>
    /// <param name="storage">The backing storage.</param>
    /// <param name="worldGuid">The world identifier.</param>
    /// <param name="sessionGuid">The unique session identifier.</param>
    /// <param name="codec">The compression codec used for spilled chunks (default <see cref="ChunkCodecId.Lz4"/>).</param>
    /// <param name="codecs">Optional shared codec set; a dedicated set is created and owned when <see langword="null"/>.</param>
    public SpillStore(
        IRandomAccessStorage storage,
        Guid worldGuid,
        Guid sessionGuid,
        ChunkCodecId codec = ChunkCodecId.Lz4,
        ChunkCodecSet? codecs = null)
    {
        ArgumentNullException.ThrowIfNull(storage);
        _storage = storage;
        _worldGuid = worldGuid;
        _sessionGuid = sessionGuid;
        _codecId = codec;
        _ownsCodecs = codecs is null;
        _codecs = codecs ?? new ChunkCodecSet();
        _ = _codecs.GetCodec(codec);
        _relativePath = StreamingSavePaths.SpillFilePath(worldGuid, sessionGuid);
    }

    /// <summary>Gets the world identifier.</summary>
    public Guid WorldGuid => _worldGuid;

    /// <summary>Gets the unique session identifier.</summary>
    public Guid SessionGuid => _sessionGuid;

    /// <summary>Gets the compression codec used for spilled records.</summary>
    public ChunkCodecId Codec => _codecId;

    /// <summary>Gets or sets the maximum allowed uncompressed chunk size when reading spill records.</summary>
    public int MaxUncompressedChunkBytes { get; set; } = StreamingSaveFormat.DefaultMaxUncompressedChunkBytes;

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
        byte schemaVersion = StreamingSaveFormat.DefaultChunkSchemaVersion)
    {
        IChunkCodec codec = _codecs.GetCodec(_codecId);
        int maxCompressed = codec.GetMaxCompressedLength(uncompressedPayload.Length);
        byte[]? compBuffer = maxCompressed > 0 ? ArrayPool<byte>.Shared.Rent(maxCompressed) : null;
        try
        {
            Span<byte> compSpan = compBuffer is not null ? compBuffer.AsSpan(0, maxCompressed) : Span<byte>.Empty;
            int compLength = codec.Compress(uncompressedPayload, compSpan);
            long alignedLengthLong = StreamingSaveFormat.AlignedChunkRecordLength((uint)compLength);
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

                    int written = StreamingSaveFormat.WriteChunkRecord(recordBuffer.AsSpan(0, alignedLength), entry, compSpan[..compLength]);
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
    /// <param name="failureReason">The corruption or I/O error reason when returning <see cref="SpillReadStatus.Failed"/>.</param>
    /// <returns>The read outcome distinguishing missing entries from verification failures.</returns>
    public SpillReadStatus TryReadChunk(
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
                return SpillReadStatus.NotFound;
            }

            if (_handle is null)
            {
                failureReason = $"Spill handle for session {_sessionGuid:N} is unavailable.";
                return SpillReadStatus.Failed;
            }

            return StreamingSaveFormat.TryReadAndDecompressChunkRecord(
                _handle,
                entry.RecordOffset,
                entry,
                _codecs,
                MaxUncompressedChunkBytes,
                uncompressedDestination,
                out failureReason)
                ? SpillReadStatus.Success
                : SpillReadStatus.Failed;
        }
        finally
        {
            _rwLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Reads <paramref name="key"/> from this spill log first, falling back to <paramref name="segmentStore"/> only when not spilled.
    /// </summary>
    /// <param name="key">The chunk key.</param>
    /// <param name="baselineIndex">The committed baseline index for the active session.</param>
    /// <param name="segmentStore">The committed segment store.</param>
    /// <param name="uncompressedDestination">The destination writer.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if read and verified cleanly; <see langword="false"/> if missing or corrupted.</returns>
    public bool TryReadChunkOrFallback(
        ChunkKey key,
        IReadOnlyDictionary<ChunkKey, ChunkIndexEntry> baselineIndex,
        ChunkSegmentStore segmentStore,
        IBufferWriter<byte> uncompressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(baselineIndex);
        ArgumentNullException.ThrowIfNull(segmentStore);
        ArgumentNullException.ThrowIfNull(uncompressedDestination);

        SpillReadStatus status = TryReadChunk(key, uncompressedDestination, out _, out string? spillFailure);
        if (status == SpillReadStatus.Success)
        {
            failureReason = null;
            return true;
        }

        if (status == SpillReadStatus.Failed)
        {
            failureReason = spillFailure ?? $"Failed to read spilled chunk {key}.";
            return false;
        }

        if (!baselineIndex.TryGetValue(key, out ChunkIndexEntry baselineEntry))
        {
            failureReason = $"Chunk {key} not found in spill store or baseline index.";
            return false;
        }

        return segmentStore.TryReadChunk(baselineEntry, uncompressedDestination, out failureReason);
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
    /// Verifies and transfers spilled chunks (except <paramref name="excludeKeys"/>) into <paramref name="targetStore"/>.
    /// </summary>
    /// <param name="targetStore">The committed segment store receiving drained chunks.</param>
    /// <param name="targetCodec">Optional target codec; <see langword="null"/> preserves each spilled chunk's codec.</param>
    /// <param name="flushToDisk">Whether to flush target segments to durable storage.</param>
    /// <param name="excludeKeys">Optional keys superseded by newer in-memory state.</param>
    /// <returns>The committed <see cref="ChunkIndexEntry"/> array sorted in <see cref="ChunkKey"/> order.</returns>
    public ChunkIndexEntry[] DrainDirtyToSegmentStore(
        ChunkSegmentStore targetStore,
        ChunkCodecId? targetCodec = null,
        bool flushToDisk = true,
        IReadOnlySet<ChunkKey>? excludeKeys = null)
    {
        ArgumentNullException.ThrowIfNull(targetStore);
        if (targetCodec.HasValue)
            _ = _codecs.GetCodec(targetCodec.Value);

        _rwLock.EnterWriteLock();
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_latestEntries.Count == 0)
                return [];

            var toDrain = new List<ChunkIndexEntry>(_latestEntries.Count);
            foreach ((ChunkKey key, ChunkIndexEntry entry) in _latestEntries)
            {
                if (excludeKeys is not null && excludeKeys.Contains(key))
                    continue;

                toDrain.Add(entry);
            }

            if (toDrain.Count == 0)
                return [];

            if (_handle is null)
                throw new InvalidOperationException("Spill entries exist without an open spill handle.");

            toDrain.Sort(static (a, b) => a.Key.CompareTo(b.Key));
            var batch = new PrecompressedChunkWrite[toDrain.Count];
            var scratch = new ArrayBufferWriter<byte>(64 * 1024);

            for (int i = 0; i < toDrain.Count; i++)
            {
                ChunkIndexEntry entry = toDrain[i];
                scratch.Clear();

                if (targetCodec is null || targetCodec.Value == entry.Codec)
                {
                    if (!StreamingSaveFormat.TryReadVerifiedCompressedRecord(_handle, entry.RecordOffset, entry, scratch, out string? failureReason))
                        throw new InvalidDataException($"Spill record verification failed for {entry.Key}: {failureReason}");

                    batch[i] = new PrecompressedChunkWrite(
                        entry.Key,
                        scratch.WrittenSpan.ToArray(),
                        entry.UncompressedSize,
                        entry.Codec,
                        entry.Flags,
                        entry.SchemaVersion);
                }
                else
                {
                    if (!StreamingSaveFormat.TryReadAndDecompressChunkRecord(
                            _handle,
                            entry.RecordOffset,
                            entry,
                            _codecs,
                            MaxUncompressedChunkBytes,
                            scratch,
                            out string? failureReason))
                    {
                        throw new InvalidDataException($"Spill record decode failed for {entry.Key}: {failureReason}");
                    }

                    IChunkCodec codecImpl = _codecs.GetCodec(targetCodec.Value);
                    ReadOnlySpan<byte> uncompressed = scratch.WrittenSpan;
                    int maxComp = codecImpl.GetMaxCompressedLength(uncompressed.Length);
                    byte[] compBuf = maxComp > 0 ? new byte[maxComp] : [];
                    int compLen = codecImpl.Compress(uncompressed, compBuf);
                    batch[i] = new PrecompressedChunkWrite(
                        entry.Key,
                        compBuf.AsMemory(0, compLen),
                        entry.UncompressedSize,
                        targetCodec.Value,
                        entry.Flags,
                        entry.SchemaVersion);
                }
            }

            ChunkIndexEntry[] committed = targetStore.AppendPrecompressedBatch(batch, flushToDisk);
            for (int i = 0; i < toDrain.Count; i++)
                _latestEntries.Remove(toDrain[i].Key);

            if (_latestEntries.Count == 0)
                CloseAndDeleteSpillFileLocked();

            return committed;
        }
        finally
        {
            _rwLock.ExitWriteLock();
        }
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
        if (_ownsCodecs)
            _codecs.Dispose();

        _rwLock.Dispose();
    }

    private void EnsureSpillHandleOpenLocked()
    {
        if (_handle is not null)
            return;

        _storage.DeleteFile(_relativePath);
        IRandomAccessHandle handle = _storage.OpenAppendOrCreate(_relativePath);
        try
        {
            Span<byte> header = stackalloc byte[StreamingSaveFormat.SegmentHeaderSize];
            StreamingSaveFormat.WriteSegmentHeader(header, SegmentFlags.Spill, 0, _worldGuid, DateTime.UtcNow.Ticks);
            handle.WriteAt(0, header);
        }
        catch
        {
            handle.Dispose();
            _storage.DeleteFile(_relativePath);
            throw;
        }

        _handle = handle;
        _cursor = StreamingSaveFormat.SegmentHeaderSize;
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
