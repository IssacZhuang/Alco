using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

/// <summary>
/// Uniquely identifies a persisted map chunk and orders chunks by <see cref="MapId"/> then Morton Z-order.
/// </summary>
/// <param name="MapId">The monotonic numeric map identifier.</param>
/// <param name="ChunkX">The signed chunk X coordinate.</param>
/// <param name="ChunkY">The signed chunk Y coordinate.</param>
public readonly record struct ChunkKey(uint MapId, int ChunkX, int ChunkY) : IComparable<ChunkKey>
{
    /// <summary>
    /// Gets the 64-bit unsigned Morton code for <see cref="ChunkX"/> and <see cref="ChunkY"/>.
    /// </summary>
    public ulong MortonCode => Morton2D.Encode(ChunkX, ChunkY);

    /// <inheritdoc/>
    public int CompareTo(ChunkKey other)
    {
        int mapCompare = MapId.CompareTo(other.MapId);
        if (mapCompare != 0)
            return mapCompare;

        return Morton2D.Encode(ChunkX, ChunkY).CompareTo(Morton2D.Encode(other.ChunkX, other.ChunkY));
    }
}

/// <summary>
/// Identifies a specific physical chunk record inside a committed segment file.
/// </summary>
/// <param name="SegmentId">The committed segment sequence number.</param>
/// <param name="RecordOffset">The byte offset of the 32-byte chunk record header within the segment file.</param>
public readonly record struct ChunkRecordLocation(uint SegmentId, uint RecordOffset);

/// <summary>
/// Tri-state outcome for chunk reads distinguishing missing chunks from checksum or I/O failures.
/// </summary>
public enum ChunkReadStatus : byte
{
    /// <summary>No persisted record exists for the requested chunk.</summary>
    NotFound = 0,

    /// <summary>The chunk record was verified and decoded cleanly.</summary>
    Success = 1,

    /// <summary>A persisted record existed, but reading, verifying, or decompressing it failed.</summary>
    Failed = 2
}

/// <summary>
/// Thread-safe read abstraction over committed chunk segment stores and portable <c>.asav</c> archives.
/// </summary>
public interface IChunkReader
{
    /// <summary>
    /// Reads, verifies, and decompresses the chunk described by <paramref name="entry"/>.
    /// </summary>
    /// <param name="entry">The chunk index entry locating the record.</param>
    /// <param name="uncompressedDestination">The buffer writer receiving the uncompressed payload.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if verified and decompressed cleanly; otherwise, <see langword="false"/>.</returns>
    bool TryReadChunk(
        in ChunkIndexEntry entry,
        IBufferWriter<byte> uncompressedDestination,
        [NotNullWhen(false)] out string? failureReason);

    /// <summary>
    /// Reads and verifies the compressed chunk payload for <paramref name="entry"/> without decompressing it.
    /// </summary>
    /// <param name="entry">The chunk index entry locating the record.</param>
    /// <param name="compressedDestination">The buffer writer receiving the verified compressed payload.</param>
    /// <param name="failureReason">The failure reason when returning <see langword="false"/>.</param>
    /// <returns><see langword="true"/> if verified and read cleanly; otherwise, <see langword="false"/>.</returns>
    bool TryReadCompressedChunk(
        in ChunkIndexEntry entry,
        IBufferWriter<byte> compressedDestination,
        [NotNullWhen(false)] out string? failureReason);
}
