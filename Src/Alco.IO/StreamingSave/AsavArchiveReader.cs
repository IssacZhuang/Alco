using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

/// <summary>
/// Thread-safe read-only streaming reader for portable <c>.asav</c> world save archives.
/// </summary>
/// <typeparam name="TMeta">The snapshot metadata type implementing <see cref="ISerializable"/>.</typeparam>
public sealed class AsavArchiveReader<TMeta> : IDisposable where TMeta : ISerializable, new()
{
    private readonly IRandomAccessHandle _handle;
    private readonly bool _ownsHandle;
    private readonly ChunkCodecSet _codecs;
    private readonly bool _ownsCodecs;
    private readonly Guid _worldGuid;
    private readonly TMeta _meta;
    private readonly byte[]? _thumbnailBytes;
    private readonly byte[] _coreBytes;
    private readonly ChunkCodecId _coreCodec;
    private readonly ChunkIndexEntry[] _chunkIndex;
    private readonly uint _chunkDataOffset;
    private readonly long _chunkDataSectionLength;
    private volatile bool _disposed;

    private AsavArchiveReader(
        IRandomAccessHandle handle,
        bool ownsHandle,
        ChunkCodecSet codecs,
        bool ownsCodecs,
        Guid worldGuid,
        TMeta meta,
        byte[]? thumbnailBytes,
        byte[] coreBytes,
        ChunkCodecId coreCodec,
        ChunkIndexEntry[] chunkIndex,
        uint chunkDataOffset,
        long chunkDataSectionLength)
    {
        _handle = handle;
        _ownsHandle = ownsHandle;
        _codecs = codecs;
        _ownsCodecs = ownsCodecs;
        _worldGuid = worldGuid;
        _meta = meta;
        _thumbnailBytes = thumbnailBytes;
        _coreBytes = coreBytes;
        _coreCodec = coreCodec;
        _chunkIndex = chunkIndex;
        _chunkDataOffset = chunkDataOffset;
        _chunkDataSectionLength = chunkDataSectionLength;
    }

    /// <summary>Gets the archived world identifier.</summary>
    public Guid WorldGuid => _worldGuid;

    /// <summary>Gets the decoded snapshot metadata.</summary>
    public TMeta Meta => _meta;

    /// <summary>Gets the embedded PNG thumbnail bytes, or <see langword="null"/> if absent.</summary>
    public byte[]? ThumbnailBytes => _thumbnailBytes;

    /// <summary>Gets the decompressed core world state bytes.</summary>
    public byte[] CoreBytes => _coreBytes;

    /// <summary>Gets the compression codec used for <see cref="CoreBytes"/> in the embedded snapshot.</summary>
    public ChunkCodecId CoreCodec => _coreCodec;

    /// <summary>Gets the Morton-ordered chunk index entries inside this archive.</summary>
    public IReadOnlyList<ChunkIndexEntry> ChunkIndex => _chunkIndex;

    /// <summary>Gets or sets the maximum allowed uncompressed chunk size when reading archived chunks.</summary>
    public int MaxUncompressedChunkBytes { get; set; } = StreamingSaveFormat.DefaultMaxUncompressedChunkBytes;

    /// <summary>
    /// Attempts to open and validate an external <c>.asav</c> archive file on disk for streaming reads.
    /// </summary>
    /// <param name="filePath">The path to the <c>.asav</c> file.</param>
    /// <param name="reader">The opened reader on success.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <param name="verifyWholeFileHash">Whether to verify the whole-file <c>XxHash64</c> checksum on open.</param>
    /// <param name="codecs">Optional shared codec set.</param>
    /// <returns><see langword="true"/> if opened and validated; otherwise, <see langword="false"/>.</returns>
    public static bool TryOpenFile(
        string filePath,
        [NotNullWhen(true)] out AsavArchiveReader<TMeta>? reader,
        [NotNullWhen(false)] out string? failureReason,
        bool verifyWholeFileHash = true,
        ChunkCodecSet? codecs = null)
    {
        if (!DirectoryRandomAccessStorage.TryOpenExternalRead(filePath, out IRandomAccessHandle? handle))
        {
            reader = null;
            failureReason = $"Archive file '{filePath}' could not be opened.";
            return false;
        }

        if (TryOpenHandle(handle, ownsHandle: true, out reader, out failureReason, verifyWholeFileHash, codecs))
            return true;

        handle.Dispose();
        return false;
    }

    /// <summary>
    /// Attempts to open and validate an <c>.asav</c> archive from an existing <see cref="IRandomAccessHandle"/>.
    /// </summary>
    /// <param name="handle">The readable archive handle.</param>
    /// <param name="ownsHandle">Whether disposing the reader also disposes <paramref name="handle"/>.</param>
    /// <param name="reader">The opened reader on success.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <param name="verifyWholeFileHash">Whether to verify the whole-file <c>XxHash64</c> checksum on open.</param>
    /// <param name="codecs">Optional shared codec set.</param>
    /// <param name="maxUncompressedCoreBytes">Maximum permitted uncompressed core snapshot size.</param>
    /// <returns><see langword="true"/> if opened and validated; otherwise, <see langword="false"/>.</returns>
    public static bool TryOpenHandle(
        IRandomAccessHandle handle,
        bool ownsHandle,
        [NotNullWhen(true)] out AsavArchiveReader<TMeta>? reader,
        [NotNullWhen(false)] out string? failureReason,
        bool verifyWholeFileHash = true,
        ChunkCodecSet? codecs = null,
        int maxUncompressedCoreBytes = StreamingSaveFormat.DefaultMaxUncompressedCoreBytes)
    {
        ArgumentNullException.ThrowIfNull(handle);
        reader = null;

        long totalLength = handle.Length;
        if (totalLength < StreamingSaveFormat.AsavHeaderSize + StreamingSaveFormat.WholeFileTrailerSize)
        {
            failureReason = "Archive is shorter than minimum ASAV header and trailer.";
            return false;
        }

        Span<byte> asavHeader = stackalloc byte[StreamingSaveFormat.AsavHeaderSize];
        if (handle.ReadAt(0, asavHeader) != StreamingSaveFormat.AsavHeaderSize)
        {
            failureReason = "Failed to read 32-byte ASAV header.";
            return false;
        }

        if (!StreamingSaveFormat.TryReadAsavHeader(
                asavHeader,
                totalLength,
                out Guid worldGuid,
                out uint snapByteLength,
                out uint chunkDataOffset,
                out failureReason))
        {
            return false;
        }

        if (verifyWholeFileHash && !StreamingSaveFormat.TryVerifyWholeFileXxHash64(handle, out failureReason))
            return false;

        if (snapByteLength > int.MaxValue)
        {
            failureReason = "Embedded snapshot exceeds maximum addressable byte length.";
            return false;
        }

        byte[] snapBytes = new byte[snapByteLength];
        if (handle.ReadAt(StreamingSaveFormat.AsavHeaderSize, snapBytes) != snapBytes.Length)
        {
            failureReason = "Failed to read embedded snapshot bytes from ASAV archive.";
            return false;
        }

        long chunkDataSectionLength = (totalLength - StreamingSaveFormat.WholeFileTrailerSize) - chunkDataOffset;
        bool ownsCodecs = codecs is null;
        ChunkCodecSet activeCodecs = codecs ?? new ChunkCodecSet();
        try
        {
            if (!SnapshotImageUtility.TryParseSnapshotImage<TMeta>(
                    snapBytes,
                    worldGuid,
                    activeCodecs,
                    maxUncompressedCoreBytes,
                    verifyWholeImageHash: true,
                    requireZeroSegmentId: true,
                    chunkDataSectionLength,
                    includeThumbnail: true,
                    out SnapshotHeaderData snapHeader,
                    out TMeta? meta,
                    out byte[]? coreBytes,
                    out ChunkIndexEntry[]? chunkIndex,
                    out byte[]? thumbnailBytes,
                    out failureReason))
            {
                if (ownsCodecs)
                    activeCodecs.Dispose();
                return false;
            }

            reader = new AsavArchiveReader<TMeta>(
                handle,
                ownsHandle,
                activeCodecs,
                ownsCodecs,
                worldGuid,
                meta,
                thumbnailBytes,
                coreBytes,
                snapHeader.CoreCodec,
                chunkIndex,
                chunkDataOffset,
                chunkDataSectionLength);
            return true;
        }
        catch
        {
            if (ownsCodecs)
                activeCodecs.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Reads, verifies, and decompresses the chunk specified by <paramref name="entry"/> in a thread-safe manner.
    /// </summary>
    /// <param name="entry">An entry from <see cref="ChunkIndex"/>.</param>
    /// <param name="uncompressedDestination">The buffer writer receiving the uncompressed chunk payload.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if read and decompressed cleanly; otherwise, <see langword="false"/>.</returns>
    public bool TryReadChunk(
        in ChunkIndexEntry entry,
        IBufferWriter<byte> uncompressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(uncompressedDestination);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ValidateArchiveChunkBounds(entry, out failureReason))
            return false;

        return StreamingSaveFormat.TryReadAndDecompressChunkRecord(
            _handle,
            (long)_chunkDataOffset + entry.RecordOffset,
            entry,
            _codecs,
            MaxUncompressedChunkBytes,
            uncompressedDestination,
            out failureReason);
    }

    /// <summary>
    /// Reads and verifies the compressed chunk payload specified by <paramref name="entry"/> without decompressing it.
    /// </summary>
    /// <param name="entry">An entry from <see cref="ChunkIndex"/>.</param>
    /// <param name="compressedDestination">The buffer writer receiving the verified compressed payload.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if verified and read cleanly; otherwise, <see langword="false"/>.</returns>
    public bool TryReadCompressedChunk(
        in ChunkIndexEntry entry,
        IBufferWriter<byte> compressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        ArgumentNullException.ThrowIfNull(compressedDestination);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!ValidateArchiveChunkBounds(entry, out failureReason))
            return false;

        return StreamingSaveFormat.TryReadVerifiedCompressedRecord(
            _handle,
            (long)_chunkDataOffset + entry.RecordOffset,
            entry,
            compressedDestination,
            out failureReason);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_ownsHandle)
            _handle.Dispose();
        if (_ownsCodecs)
            _codecs.Dispose();
    }

    private bool ValidateArchiveChunkBounds(in ChunkIndexEntry entry, [NotNullWhen(false)] out string? failureReason)
    {
        if (entry.SegmentId != 0)
        {
            failureReason = $"Archive entry {entry.Key} must have SegmentId 0, got {entry.SegmentId}.";
            return false;
        }

        long recordEnd = (long)entry.RecordOffset +
                         StreamingSaveFormat.ChunkRecordHeaderSize +
                         entry.CompressedSize +
                         StreamingSaveFormat.ChunkRecordTrailerSize;
        if (recordEnd > _chunkDataSectionLength)
        {
            failureReason = $"Archive chunk {entry.Key} record exceeds archive data section bounds.";
            return false;
        }

        failureReason = null;
        return true;
    }
}
