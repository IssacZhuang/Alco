using System.Runtime.InteropServices;

namespace Alco.IO;

/// <summary>
/// Fixed 32-byte blittable index entry locating a chunk record inside a segment or <c>.asav</c> archive.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct ChunkIndexEntry
{
    private readonly uint _mapId;
    private readonly int _chunkX;
    private readonly int _chunkY;
    private readonly uint _segmentId;
    private readonly uint _recordOffset;
    private readonly uint _compressedSize;
    private readonly uint _uncompressedSize;
    private readonly ChunkCodecId _codec;
    private readonly byte _flags;
    private readonly byte _schemaVersion;
    private readonly byte _reserved;

    /// <summary>
    /// Initializes a new <see cref="ChunkIndexEntry"/>.
    /// </summary>
    /// <param name="mapId">The numeric map identifier.</param>
    /// <param name="chunkX">The signed chunk X coordinate.</param>
    /// <param name="chunkY">The signed chunk Y coordinate.</param>
    /// <param name="segmentId">The segment file ID (<c>0</c> for spill or <c>.asav</c> archive entries).</param>
    /// <param name="recordOffset">The byte offset of the record header.</param>
    /// <param name="compressedSize">The compressed payload size in bytes.</param>
    /// <param name="uncompressedSize">The uncompressed payload size in bytes.</param>
    /// <param name="codec">The compression codec used for the payload.</param>
    /// <param name="flags">Optional chunk hint flags.</param>
    /// <param name="schemaVersion">The chunk payload schema version.</param>
    public ChunkIndexEntry(
        uint mapId,
        int chunkX,
        int chunkY,
        uint segmentId,
        uint recordOffset,
        uint compressedSize,
        uint uncompressedSize,
        ChunkCodecId codec,
        byte flags = 0,
        byte schemaVersion = 1)
    {
        _mapId = mapId;
        _chunkX = chunkX;
        _chunkY = chunkY;
        _segmentId = segmentId;
        _recordOffset = recordOffset;
        _compressedSize = compressedSize;
        _uncompressedSize = uncompressedSize;
        _codec = codec;
        _flags = flags;
        _schemaVersion = schemaVersion;
        _reserved = 0;
    }

    /// <summary>Gets the numeric map identifier.</summary>
    public uint MapId => _mapId;

    /// <summary>Gets the signed chunk X coordinate.</summary>
    public int ChunkX => _chunkX;

    /// <summary>Gets the signed chunk Y coordinate.</summary>
    public int ChunkY => _chunkY;

    /// <summary>Gets the segment sequence number (<c>0</c> for spill or <c>.asav</c>).</summary>
    public uint SegmentId => _segmentId;

    /// <summary>Gets the byte offset of the chunk record header.</summary>
    public uint RecordOffset => _recordOffset;

    /// <summary>Gets the compressed payload length in bytes.</summary>
    public uint CompressedSize => _compressedSize;

    /// <summary>Gets the uncompressed payload length in bytes.</summary>
    public uint UncompressedSize => _uncompressedSize;

    /// <summary>Gets the compression codec identifier.</summary>
    public ChunkCodecId Codec => _codec;

    /// <summary>Gets the chunk hint flags.</summary>
    public byte Flags => _flags;

    /// <summary>Gets the chunk payload schema version.</summary>
    public byte SchemaVersion => _schemaVersion;

    /// <summary>Gets the logical chunk key.</summary>
    public ChunkKey Key => new(_mapId, _chunkX, _chunkY);

    /// <summary>Gets the physical record location within a segment store.</summary>
    public ChunkRecordLocation Location => new(_segmentId, _recordOffset);

    /// <summary>
    /// Returns a copy of this entry updated with a new physical storage location and encoding.
    /// </summary>
    /// <param name="segmentId">The new segment ID.</param>
    /// <param name="recordOffset">The new record offset.</param>
    /// <param name="compressedSize">The new compressed payload length.</param>
    /// <param name="codec">The new compression codec.</param>
    /// <returns>The updated index entry.</returns>
    public ChunkIndexEntry WithLocation(uint segmentId, uint recordOffset, uint compressedSize, ChunkCodecId codec) =>
        new(_mapId, _chunkX, _chunkY, segmentId, recordOffset, compressedSize, _uncompressedSize, codec, _flags, _schemaVersion);
}
