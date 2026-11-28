using System.Runtime.InteropServices;

namespace Alco.IO;

/// <summary>
/// Bit flags stored in <see cref="ChunkIndexEntry.Flags"/>.
/// </summary>
[Flags]
public enum ChunkIndexFlags : byte
{
    /// <summary>No special chunk hint flags.</summary>
    None = 0,

    /// <summary>Chunk payload contains at least one serialized entity.</summary>
    HasEntities = 1 << 0,

    /// <summary>Chunk lighting/height hint is dirty or needs refresh.</summary>
    DirtyLight = 1 << 1,

    /// <summary>Chunk was generated and confirmed empty (no physical payload stored).</summary>
    EmptyGenerated = 1 << 2
}

/// <summary>
/// Fixed 32-byte blittable index entry locating a chunk record inside a segment, spill log, or <c>.asav</c> archive.
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
    /// <param name="segmentId">The segment file ID (<c>0</c> for spill, <c>.asav</c> archive, or <see cref="ChunkIndexFlags.EmptyGenerated"/> entries).</param>
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

    /// <summary>Gets the segment sequence number (<c>0</c> for spill, <c>.asav</c>, or empty-generated entries).</summary>
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

    /// <summary>Gets whether this entry marks a generated empty chunk with no physical payload.</summary>
    public bool IsEmptyGenerated => (_flags & (byte)ChunkIndexFlags.EmptyGenerated) != 0;

    /// <summary>Gets whether an <see cref="IsEmptyGenerated"/> entry satisfies zero-payload invariants.</summary>
    public bool IsValidEmptyGenerated =>
        IsEmptyGenerated &&
        _segmentId == 0 &&
        _recordOffset == 0 &&
        _compressedSize == 0 &&
        _uncompressedSize == 0 &&
        _codec == ChunkCodecId.None;

    /// <summary>
    /// Creates an index entry representing an empty generated chunk with no physical payload.
    /// </summary>
    /// <param name="key">The logical chunk key.</param>
    /// <param name="flags">Optional additional hint flags.</param>
    /// <param name="schemaVersion">The chunk payload schema version.</param>
    /// <returns>The normalized empty-generated chunk index entry.</returns>
    public static ChunkIndexEntry CreateEmptyGenerated(
        ChunkKey key,
        byte flags = 0,
        byte schemaVersion = ChunkRecordCodec.DefaultChunkSchemaVersion) =>
        new(
            key.MapId,
            key.ChunkX,
            key.ChunkY,
            segmentId: 0,
            recordOffset: 0,
            compressedSize: 0,
            uncompressedSize: 0,
            ChunkCodecId.None,
            (byte)(flags | (byte)ChunkIndexFlags.EmptyGenerated),
            schemaVersion);

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
