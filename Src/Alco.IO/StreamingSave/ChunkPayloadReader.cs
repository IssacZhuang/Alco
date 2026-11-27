using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Alco.IO;

/// <summary>
/// Single decoded section slice yielded by <see cref="ChunkPayloadReader"/>.
/// </summary>
public readonly ref struct ChunkSection
{
    /// <summary>
    /// Initializes a <see cref="ChunkSection"/> view.
    /// </summary>
    /// <param name="id">The section identifier.</param>
    /// <param name="flags">The section flags.</param>
    /// <param name="data">The section payload slice.</param>
    public ChunkSection(ushort id, ushort flags, ReadOnlySpan<byte> data)
    {
        Id = id;
        Flags = flags;
        Data = data;
    }

    /// <summary>Gets the section identifier.</summary>
    public ushort Id { get; }

    /// <summary>Gets the section flags.</summary>
    public ushort Flags { get; }

    /// <summary>Gets the section payload bytes.</summary>
    public ReadOnlySpan<byte> Data { get; }
}

/// <summary>
/// Zero-allocation reader over a validated <c>ACHK</c> TLV chunk payload.
/// </summary>
public readonly ref struct ChunkPayloadReader
{
    private readonly ReadOnlySpan<byte> _payload;
    private readonly int _sectionCount;
    private readonly double _lastSimTime;

    private ChunkPayloadReader(ReadOnlySpan<byte> payload, int sectionCount, double lastSimTime)
    {
        _payload = payload;
        _sectionCount = sectionCount;
        _lastSimTime = lastSimTime;
    }

    /// <summary>
    /// Gets the absolute simulation timestamp (in seconds) recorded when the chunk was saved.
    /// </summary>
    public double LastSimTime => _lastSimTime;

    /// <summary>
    /// Gets the number of sections present in this chunk payload.
    /// </summary>
    public int SectionCount => _sectionCount;

    /// <summary>
    /// Validates and opens an <c>ACHK</c> chunk payload without heap allocation for typical section counts.
    /// </summary>
    /// <param name="payload">The uncompressed chunk payload bytes.</param>
    /// <param name="reader">The opened reader on success.</param>
    /// <returns><see langword="true"/> if the header, section table, and total length are valid; otherwise, <see langword="false"/>.</returns>
    public static bool TryOpen(ReadOnlySpan<byte> payload, out ChunkPayloadReader reader)
    {
        reader = default;
        if (payload.Length < ChunkRecordCodec.ChunkPayloadHeaderSize)
            return false;

        if (!payload[..4].SequenceEqual(ChunkRecordCodec.ChunkPayloadMagicBytes))
            return false;

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
        if (version != ChunkRecordCodec.ChunkPayloadVersion)
            return false;

        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
        double lastSimTime = BinaryPrimitives.ReadDoubleLittleEndian(payload.Slice(8, 8));

        long headerAndTableLength = ChunkRecordCodec.ChunkPayloadHeaderSize +
                                    (long)sectionCount * ChunkRecordCodec.ChunkSectionDescSize;
        if (headerAndTableLength > payload.Length)
            return false;

        Span<ushort> seenStack = sectionCount <= 64 ? stackalloc ushort[sectionCount] : default;
        HashSet<ushort>? seenHeap = sectionCount > 64 ? new HashSet<ushort>(sectionCount) : null;

        long dataCursor = headerAndTableLength;
        for (int i = 0; i < sectionCount; i++)
        {
            int descOffset = ChunkRecordCodec.ChunkPayloadHeaderSize + i * ChunkRecordCodec.ChunkSectionDescSize;
            ushort sectionId = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(descOffset, 2));
            uint byteLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(descOffset + 4, 4));

            if (seenHeap is null)
            {
                for (int j = 0; j < i; j++)
                {
                    if (seenStack[j] == sectionId)
                        return false;
                }

                seenStack[i] = sectionId;
            }
            else if (!seenHeap.Add(sectionId))
            {
                return false;
            }

            long remaining = payload.Length - dataCursor;
            if (byteLength > (ulong)remaining)
                return false;

            dataCursor += byteLength;
        }

        if (dataCursor != payload.Length)
            return false;

        reader = new ChunkPayloadReader(payload, sectionCount, lastSimTime);
        return true;
    }

    /// <summary>
    /// Attempts to locate a section by <paramref name="sectionId"/>.
    /// </summary>
    /// <param name="sectionId">The section identifier to look up.</param>
    /// <param name="data">The section bytes on success.</param>
    /// <param name="flags">The section flags on success.</param>
    /// <returns><see langword="true"/> if the section was present; otherwise, <see langword="false"/>.</returns>
    public bool TryGetSection(ushort sectionId, out ReadOnlySpan<byte> data, out ushort flags)
    {
        int dataOffset = ChunkRecordCodec.ChunkPayloadHeaderSize + _sectionCount * ChunkRecordCodec.ChunkSectionDescSize;
        for (int i = 0; i < _sectionCount; i++)
        {
            int descOffset = ChunkRecordCodec.ChunkPayloadHeaderSize + i * ChunkRecordCodec.ChunkSectionDescSize;
            ushort currentId = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(descOffset, 2));
            ushort currentFlags = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(descOffset + 2, 2));
            int byteLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(descOffset + 4, 4));

            if (currentId == sectionId)
            {
                flags = currentFlags;
                data = _payload.Slice(dataOffset, byteLength);
                return true;
            }

            dataOffset += byteLength;
        }

        data = default;
        flags = 0;
        return false;
    }

    /// <summary>
    /// Returns a zero-allocation sequential enumerator over all sections in header order.
    /// </summary>
    /// <returns>An enumerator yielding each <see cref="ChunkSection"/> in O(N) total time.</returns>
    public Enumerator GetEnumerator() => new(_payload, _sectionCount);

    /// <summary>
    /// Sequential zero-allocation enumerator over sections in a <see cref="ChunkPayloadReader"/>.
    /// </summary>
    public ref struct Enumerator
    {
        private readonly ReadOnlySpan<byte> _payload;
        private readonly int _sectionCount;
        private int _index;
        private int _dataOffset;
        private ChunkSection _current;

        internal Enumerator(ReadOnlySpan<byte> payload, int sectionCount)
        {
            _payload = payload;
            _sectionCount = sectionCount;
            _index = -1;
            _dataOffset = ChunkRecordCodec.ChunkPayloadHeaderSize + sectionCount * ChunkRecordCodec.ChunkSectionDescSize;
            _current = default;
        }

        /// <summary>Gets the current section slice.</summary>
        public readonly ChunkSection Current => _current;

        /// <summary>Advances to the next section.</summary>
        /// <returns><see langword="true"/> if another section is present; otherwise, <see langword="false"/>.</returns>
        public bool MoveNext()
        {
            int nextIndex = _index + 1;
            if (nextIndex >= _sectionCount)
                return false;

            int descOffset = ChunkRecordCodec.ChunkPayloadHeaderSize + nextIndex * ChunkRecordCodec.ChunkSectionDescSize;
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(descOffset, 2));
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(descOffset + 2, 2));
            int byteLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(descOffset + 4, 4));

            _current = new ChunkSection(id, flags, _payload.Slice(_dataOffset, byteLength));
            _dataOffset += byteLength;
            _index = nextIndex;
            return true;
        }
    }
}
