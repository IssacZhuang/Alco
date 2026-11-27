using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Alco.IO;

/// <summary>
/// Uniquely identifies a world save slot snapshot by <see cref="WorldGuid"/> and <see cref="SlotId"/>.
/// </summary>
/// <param name="WorldGuid">The world identifier.</param>
/// <param name="SlotId">The slot filename stem without <c>.snap</c>.</param>
public readonly record struct SnapshotId(Guid WorldGuid, string SlotId);

/// <summary>
/// Lightweight snapshot listing record produced by reading only the snapshot header and metadata.
/// </summary>
/// <typeparam name="TMeta">The metadata type.</typeparam>
/// <param name="Id">The snapshot identifier.</param>
/// <param name="Meta">The decoded metadata object.</param>
/// <param name="HasThumbnail">Whether the snapshot includes an embedded PNG thumbnail.</param>
public readonly record struct SnapshotMetaInfo<TMeta>(
    SnapshotId Id,
    TMeta Meta,
    bool HasThumbnail) where TMeta : ISerializable, new();

/// <summary>
/// Full snapshot payload loaded from a <c>.snap</c> file after verifying all checksums.
/// </summary>
/// <typeparam name="TMeta">The metadata type.</typeparam>
public sealed class SnapshotLoadResult<TMeta> where TMeta : ISerializable, new()
{
    /// <summary>
    /// Initializes a new <see cref="SnapshotLoadResult{TMeta}"/>.
    /// </summary>
    /// <param name="id">The snapshot identifier.</param>
    /// <param name="meta">The decoded metadata.</param>
    /// <param name="coreBytes">The decompressed core world state bytes.</param>
    /// <param name="coreCodec">The compression codec used for core bytes in the snapshot.</param>
    /// <param name="chunkIndex">The Morton-ordered chunk index entries.</param>
    /// <param name="thumbnailBytes">The optional PNG thumbnail bytes.</param>
    public SnapshotLoadResult(
        SnapshotId id,
        TMeta meta,
        ReadOnlyMemory<byte> coreBytes,
        ChunkCodecId coreCodec,
        IReadOnlyList<ChunkIndexEntry> chunkIndex,
        ReadOnlyMemory<byte> thumbnailBytes)
    {
        Id = id;
        Meta = meta;
        CoreBytes = coreBytes;
        CoreCodec = coreCodec;
        ChunkIndex = chunkIndex;
        ThumbnailBytes = thumbnailBytes;
    }

    /// <summary>Gets the snapshot identifier.</summary>
    public SnapshotId Id { get; }

    /// <summary>Gets the decoded metadata.</summary>
    public TMeta Meta { get; }

    /// <summary>Gets the decompressed core world state bytes.</summary>
    public ReadOnlyMemory<byte> CoreBytes { get; }

    /// <summary>Gets the compression codec stored in the snapshot for <see cref="CoreBytes"/>.</summary>
    public ChunkCodecId CoreCodec { get; }

    /// <summary>Gets the Morton-ordered chunk index entries.</summary>
    public IReadOnlyList<ChunkIndexEntry> ChunkIndex { get; }

    /// <summary>Gets the embedded PNG thumbnail bytes (empty when absent).</summary>
    public ReadOnlyMemory<byte> ThumbnailBytes { get; }

    /// <summary>Gets whether a thumbnail was loaded.</summary>
    public bool HasThumbnail => !ThumbnailBytes.IsEmpty;
}

/// <summary>
/// Manages atomic <c>.snap</c> slot snapshots and world-level segment compaction over an <see cref="IRandomAccessStorage"/>.
/// </summary>
/// <typeparam name="TMeta">The snapshot metadata type implementing <see cref="ISerializable"/>.</typeparam>
public sealed class SnapshotStore<TMeta> where TMeta : ISerializable, new()
{
    private readonly IRandomAccessStorage _storage;
    private readonly ChunkCodecSet _codecs;

    /// <summary>
    /// Initializes a snapshot store backed by <paramref name="storage"/>.
    /// </summary>
    /// <param name="storage">The backing storage.</param>
    /// <param name="codecs">The shared codec set used for core compression and decompression.</param>
    public SnapshotStore(IRandomAccessStorage storage, ChunkCodecSet codecs)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(codecs);
        _storage = storage;
        _codecs = codecs;
    }

    /// <summary>Gets the underlying random-access storage.</summary>
    public IRandomAccessStorage Storage => _storage;

    /// <summary>Gets or sets the maximum allowed uncompressed core payload size when reading snapshots.</summary>
    public int MaxUncompressedCoreBytes { get; set; } = SnapshotFormat.DefaultMaxUncompressedCoreBytes;

    /// <summary>
    /// Atomically writes or replaces a <c>.snap</c> file containing metadata, optional thumbnail, core state, and Morton-sorted chunk index.
    /// </summary>
    /// <param name="id">The target snapshot identifier.</param>
    /// <param name="meta">The metadata object encoded via <see cref="PackedBinarySerializer"/>.</param>
    /// <param name="coreUncompressed">The uncompressed core world bytes.</param>
    /// <param name="chunkIndex">The live chunk index entries for this snapshot.</param>
    /// <param name="thumbnailPng">Optional PNG thumbnail bytes.</param>
    /// <param name="coreCodec">The compression codec applied to <paramref name="coreUncompressed"/>.</param>
    /// <param name="flushToDisk">Whether to flush the temporary file to disk before atomic rename.</param>
    public void WriteSnapshot(
        SnapshotId id,
        TMeta meta,
        ReadOnlySpan<byte> coreUncompressed,
        ReadOnlySpan<ChunkIndexEntry> chunkIndex,
        ReadOnlySpan<byte> thumbnailPng = default,
        ChunkCodecId coreCodec = ChunkCodecId.Lz4,
        bool flushToDisk = true)
    {
        ArgumentNullException.ThrowIfNull(meta);

        string relPath = StreamingSavePaths.SnapshotFilePath(id.WorldGuid, id.SlotId);
        ChunkIndexEntry[] sortedIndex = SnapshotFormat.SortAndValidateChunkIndex(chunkIndex);
        byte[] image = SnapshotFormat.BuildSnapshotImage(
            id.WorldGuid,
            meta,
            coreUncompressed,
            sortedIndex,
            thumbnailPng,
            coreCodec,
            _codecs);

        _storage.WriteAtomic(relPath, image, flushToDisk);
    }

    /// <summary>
    /// Enumerates snapshot metadata by reading only the 64-byte header and <typeparamref name="TMeta"/> section of each <c>.snap</c> file.
    /// </summary>
    /// <param name="worldGuid">Optional world filter; when <see langword="null"/>, all worlds are scanned.</param>
    /// <param name="onCorruptSnapshot">Optional callback invoked when a corrupt snapshot file is skipped.</param>
    /// <returns>Sorted snapshot metadata listings.</returns>
    public IReadOnlyList<SnapshotMetaInfo<TMeta>> EnumerateSnapshots(
        Guid? worldGuid = null,
        Action<string>? onCorruptSnapshot = null)
    {
        var results = new List<SnapshotMetaInfo<TMeta>>();
        if (worldGuid.HasValue)
        {
            ScanWorldSnapshots(worldGuid.Value, results, onCorruptSnapshot);
        }
        else
        {
            IReadOnlyList<string> worldDirs = _storage.EnumerateDirectories(StreamingSavePaths.WorldsDirectory);
            for (int i = 0; i < worldDirs.Count; i++)
            {
                if (StreamingSavePaths.TryParseWorldGuid(worldDirs[i], out Guid parsedGuid))
                    ScanWorldSnapshots(parsedGuid, results, onCorruptSnapshot);
            }
        }

        results.Sort(static (a, b) =>
        {
            int worldCmp = a.Id.WorldGuid.CompareTo(b.Id.WorldGuid);
            return worldCmp != 0 ? worldCmp : string.CompareOrdinal(a.Id.SlotId, b.Id.SlotId);
        });
        return results;
    }

    /// <summary>
    /// Reads only the header and metadata from a specific snapshot.
    /// </summary>
    /// <param name="id">The snapshot identifier.</param>
    /// <param name="meta">The decoded metadata on success.</param>
    /// <param name="hasThumbnail">Whether the snapshot contains an embedded thumbnail.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if read and validated cleanly; otherwise, <see langword="false"/>.</returns>
    public bool TryReadMeta(
        SnapshotId id,
        [NotNullWhen(true)] out TMeta? meta,
        out bool hasThumbnail,
        [NotNullWhen(false)] out string? failureReason)
    {
        string relPath = StreamingSavePaths.SnapshotFilePath(id.WorldGuid, id.SlotId);

        if (!_storage.TryOpenRead(relPath, out IRandomAccessHandle? handle))
        {
            meta = default;
            hasThumbnail = false;
            failureReason = $"Snapshot '{relPath}' does not exist.";
            return false;
        }

        using (handle)
        {
            return TryReadHeaderAndMetaFromHandle(handle, id.WorldGuid, out _, out meta, out hasThumbnail, out failureReason);
        }
    }

    /// <summary>
    /// Reads only the header and embedded PNG thumbnail bytes from a snapshot without reading core or chunk index sections.
    /// </summary>
    /// <param name="id">The snapshot identifier.</param>
    /// <param name="pngBytes">The PNG bytes on success.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if the snapshot exists, is valid, and contains a thumbnail; otherwise, <see langword="false"/>.</returns>
    public bool TryReadThumbnail(
        SnapshotId id,
        [NotNullWhen(true)] out byte[]? pngBytes,
        [NotNullWhen(false)] out string? failureReason)
    {
        string relPath = StreamingSavePaths.SnapshotFilePath(id.WorldGuid, id.SlotId);
        pngBytes = null;

        if (!_storage.TryOpenRead(relPath, out IRandomAccessHandle? handle))
        {
            failureReason = $"Snapshot '{relPath}' does not exist.";
            return false;
        }

        using (handle)
        {
            if (!TryReadSnapshotHeaderFromHandle(handle, id.WorldGuid, out SnapshotHeaderData header, out failureReason))
                return false;

            if ((header.Flags & SnapshotFlags.HasThumbnail) == 0 || header.ThumbLength == 0)
            {
                failureReason = $"Snapshot '{relPath}' does not contain a thumbnail.";
                return false;
            }

            if (header.ThumbLength > int.MaxValue)
            {
                failureReason = $"Snapshot '{relPath}' thumbnail exceeds addressable byte length.";
                return false;
            }

            byte[] buffer = new byte[header.ThumbLength];
            if (handle.ReadAt(header.ThumbOffset, buffer) != buffer.Length)
            {
                failureReason = $"Failed to read thumbnail bytes from '{relPath}'.";
                return false;
            }

            pngBytes = buffer;
            failureReason = null;
            return true;
        }
    }

    /// <summary>
    /// Reads and fully verifies a snapshot, returning its metadata, decompressed core bytes, and chunk index.
    /// </summary>
    /// <param name="id">The snapshot identifier.</param>
    /// <param name="result">The loaded snapshot data on success.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <param name="includeThumbnail">Whether to also copy out the embedded PNG thumbnail bytes.</param>
    /// <returns><see langword="true"/> if loaded and verified cleanly; otherwise, <see langword="false"/>.</returns>
    public bool TryLoadSnapshot(
        SnapshotId id,
        [NotNullWhen(true)] out SnapshotLoadResult<TMeta>? result,
        [NotNullWhen(false)] out string? failureReason,
        bool includeThumbnail = false)
    {
        string relPath = StreamingSavePaths.SnapshotFilePath(id.WorldGuid, id.SlotId);
        result = null;

        if (!_storage.TryReadAllBytes(relPath, out byte[]? snapBytes))
        {
            failureReason = $"Snapshot '{relPath}' does not exist.";
            return false;
        }

        if (!SnapshotFormat.TryParseSnapshotImage(
                snapBytes,
                id.WorldGuid,
                _codecs,
                MaxUncompressedCoreBytes,
                verifyWholeImageHash: true,
                requireZeroSegmentId: false,
                archiveChunkDataLength: 0,
                includeThumbnail,
                out SnapshotHeaderData header,
                out TMeta? meta,
                out byte[]? coreBytes,
                out ChunkIndexEntry[]? chunkIndex,
                out byte[]? thumbnailBytes,
                out failureReason))
        {
            return false;
        }

        result = new SnapshotLoadResult<TMeta>(
            id,
            meta,
            coreBytes,
            header.CoreCodec,
            chunkIndex,
            thumbnailBytes ?? ReadOnlyMemory<byte>.Empty);
        return true;
    }

    /// <summary>
    /// Deletes the <c>.snap</c> file identified by <paramref name="id"/> if present.
    /// </summary>
    /// <param name="id">The snapshot identifier.</param>
    /// <returns><see langword="true"/> if deleted; otherwise, <see langword="false"/>.</returns>
    public bool DeleteSnapshot(SnapshotId id)
    {
        string relPath = StreamingSavePaths.SnapshotFilePath(id.WorldGuid, id.SlotId);
        return _storage.DeleteFile(relPath);
    }

    /// <summary>
    /// Compacts segments for <paramref name="segmentStore"/>'s world while preserving every chunk referenced by retained snapshots
    /// and optional active sessions, rewriting relocated snapshot indices without decoding metadata or decompressing core payloads.
    /// </summary>
    /// <param name="segmentStore">The segment store for the target world.</param>
    /// <param name="extraLiveEntries">Additional live chunk entries held by active in-memory sessions.</param>
    /// <param name="onEntriesRelocated">Optional callback invoked during atomic commit with relocated physical locations for active sessions.</param>
    /// <param name="options">Optional compaction threshold and codec settings.</param>
    /// <returns>The compaction summary report.</returns>
    public CompactionReport CompactWorld(
        ChunkSegmentStore segmentStore,
        IEnumerable<ChunkIndexEntry>? extraLiveEntries = null,
        Action<IReadOnlyDictionary<ChunkRecordLocation, ChunkIndexEntry>>? onEntriesRelocated = null,
        CompactionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(segmentStore);
        Guid worldGuid = segmentStore.WorldGuid;

        IReadOnlyList<string> snapshotFiles = _storage.EnumerateFiles(
            StreamingSavePaths.SnapshotsDirectory(worldGuid),
            StreamingSavePaths.SnapshotFilePattern,
            recursive: false);

        var loadedSnapshots = new List<(string SlotId, RawSnapshotImage Raw)>(snapshotFiles.Count);
        var allLiveEntries = new List<ChunkIndexEntry>();

        for (int i = 0; i < snapshotFiles.Count; i++)
        {
            string relPath = snapshotFiles[i];
            if (!StreamingSavePaths.TryExtractSlotIdFromSnapshotPath(relPath, out string slotId))
                continue;

            if (!_storage.TryReadAllBytes(relPath, out byte[]? snapBytes))
                throw new InvalidDataException($"Cannot safely compact world {worldGuid:N}: snapshot '{slotId}' could not be read.");

            if (!SnapshotFormat.TryReadRawSnapshot(
                    snapBytes,
                    worldGuid,
                    verifyWholeImageHash: true,
                    requireZeroSegmentId: false,
                    archiveChunkDataLength: 0,
                    out RawSnapshotImage raw,
                    out string? failureReason))
            {
                throw new InvalidDataException($"Cannot safely compact world {worldGuid:N}: snapshot '{slotId}' is unreadable ({failureReason}).");
            }

            loadedSnapshots.Add((slotId, raw));
            allLiveEntries.AddRange(raw.ChunkIndex);
        }

        bool hasExtraLive = false;
        if (extraLiveEntries is not null)
        {
            foreach (ChunkIndexEntry extra in extraLiveEntries)
            {
                hasExtraLive = true;
                allLiveEntries.Add(extra);
            }
        }

        bool flushSnapshots = options?.FlushToDisk ?? true;
        CompactionReport report = segmentStore.Compact(
            allLiveEntries,
            relocations =>
            {
                if (relocations.Count > 0)
                {
                    for (int i = 0; i < loadedSnapshots.Count; i++)
                    {
                        (string slotId, RawSnapshotImage raw) = loadedSnapshots[i];
                        ChunkIndexEntry[]? updatedIndex = null;
                        for (int j = 0; j < raw.ChunkIndex.Length; j++)
                        {
                            if (!relocations.TryGetValue(raw.ChunkIndex[j].Location, out ChunkIndexEntry relocated))
                                continue;

                            updatedIndex ??= (ChunkIndexEntry[])raw.ChunkIndex.Clone();
                            updatedIndex[j] = relocated;
                        }

                        if (updatedIndex is not null)
                        {
                            byte[] updatedImage = SnapshotFormat.BuildFromRawSections(
                                worldGuid,
                                raw.MetaBytes.Span,
                                raw.ThumbBytes.Span,
                                raw.CompressedCoreBytes.Span,
                                raw.Header.CoreUncompressedLength,
                                raw.Header.CoreCodec,
                                updatedIndex);
                            _storage.WriteAtomic(
                                StreamingSavePaths.SnapshotFilePath(worldGuid, slotId),
                                updatedImage,
                                flushSnapshots);
                        }
                    }
                }

                onEntriesRelocated?.Invoke(relocations);
            },
            options);

        if (loadedSnapshots.Count == 0 && !hasExtraLive)
        {
            _storage.DeleteDirectoryIfEmpty(StreamingSavePaths.ChunksDirectory(worldGuid));
            _storage.DeleteDirectoryIfEmpty(StreamingSavePaths.SnapshotsDirectory(worldGuid));
            _storage.DeleteDirectoryIfEmpty(StreamingSavePaths.SessionDirectory(worldGuid));
            _storage.DeleteDirectoryIfEmpty(StreamingSavePaths.WorldDirectory(worldGuid));
        }

        return report;
    }

    private void ScanWorldSnapshots(
        Guid worldGuid,
        List<SnapshotMetaInfo<TMeta>> output,
        Action<string>? onCorruptSnapshot)
    {
        IReadOnlyList<string> files = _storage.EnumerateFiles(
            StreamingSavePaths.SnapshotsDirectory(worldGuid),
            StreamingSavePaths.SnapshotFilePattern,
            recursive: false);

        for (int i = 0; i < files.Count; i++)
        {
            string relPath = files[i];
            if (!StreamingSavePaths.TryExtractSlotIdFromSnapshotPath(relPath, out string slotId))
                continue;

            if (!_storage.TryOpenRead(relPath, out IRandomAccessHandle? handle))
                continue;

            using (handle)
            {
                if (TryReadHeaderAndMetaFromHandle(handle, worldGuid, out _, out TMeta? meta, out bool hasThumbnail, out string? failureReason))
                {
                    output.Add(new SnapshotMetaInfo<TMeta>(new SnapshotId(worldGuid, slotId), meta, hasThumbnail));
                }
                else
                {
                    onCorruptSnapshot?.Invoke($"Skipped corrupt snapshot '{relPath}': {failureReason}");
                }
            }
        }
    }

    private static bool TryReadSnapshotHeaderFromHandle(
        IRandomAccessHandle handle,
        Guid expectedWorldGuid,
        out SnapshotHeaderData header,
        [NotNullWhen(false)] out string? failureReason)
    {
        header = default;
        long fileLength = handle.Length;
        if (fileLength < SnapshotFormat.SnapshotHeaderSize + SnapshotFormat.WholeFileTrailerSize)
        {
            failureReason = "Snapshot file is shorter than minimum header and trailer size.";
            return false;
        }

        Span<byte> headerBytes = stackalloc byte[SnapshotFormat.SnapshotHeaderSize];
        if (handle.ReadAt(0, headerBytes) != SnapshotFormat.SnapshotHeaderSize)
        {
            failureReason = "Failed to read 64-byte snapshot header.";
            return false;
        }

        if (!SnapshotFormat.TryReadSnapshotHeader(headerBytes, fileLength, out header, out failureReason))
            return false;

        if (header.WorldGuid != expectedWorldGuid)
        {
            failureReason = $"Snapshot world GUID mismatch: expected {expectedWorldGuid:N}, got {header.WorldGuid:N}.";
            return false;
        }

        failureReason = null;
        return true;
    }

    private static bool TryReadHeaderAndMetaFromHandle(
        IRandomAccessHandle handle,
        Guid expectedWorldGuid,
        out SnapshotHeaderData header,
        [NotNullWhen(true)] out TMeta? meta,
        out bool hasThumbnail,
        [NotNullWhen(false)] out string? failureReason)
    {
        meta = default;
        hasThumbnail = false;
        if (!TryReadSnapshotHeaderFromHandle(handle, expectedWorldGuid, out header, out failureReason))
            return false;

        if (header.MetaLength > int.MaxValue)
        {
            failureReason = "Snapshot metadata exceeds addressable byte length.";
            return false;
        }

        byte[] metaBytes = new byte[header.MetaLength];
        if (handle.ReadAt(header.MetaOffset, metaBytes) != metaBytes.Length)
        {
            failureReason = "Failed to read snapshot metadata bytes.";
            return false;
        }

        if (!SnapshotFormat.TryDecodeMeta(metaBytes, out meta, out failureReason))
            return false;

        hasThumbnail = (header.Flags & SnapshotFlags.HasThumbnail) != 0;
        failureReason = null;
        return true;
    }
}
