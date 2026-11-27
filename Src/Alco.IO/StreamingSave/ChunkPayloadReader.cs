using System;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Alco.IO;

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
        if (payload.Length < StreamingSaveFormat.ChunkPayloadHeaderSize)
            return false;

        if (!payload[..4].SequenceEqual(StreamingSaveFormat.ChunkPayloadMagicBytes))
            return false;

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(4, 2));
        if (version != StreamingSaveFormat.ChunkPayloadVersion)
            return false;

        ushort sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(payload.Slice(6, 2));
        double lastSimTime = BinaryPrimitives.ReadDoubleLittleEndian(payload.Slice(8, 8));

        long headerAndTableLength = StreamingSaveFormat.ChunkPayloadHeaderSize +
                                    (long)sectionCount * StreamingSaveFormat.ChunkSectionDescSize;
        if (headerAndTableLength > payload.Length)
            return false;

        Span<ushort> seenStack = sectionCount <= 64 ? stackalloc ushort[sectionCount] : default;
        HashSet<ushort>? seenHeap = sectionCount > 64 ? new HashSet<ushort>(sectionCount) : null;

        long dataCursor = headerAndTableLength;
        for (int i = 0; i < sectionCount; i++)
        {
            int descOffset = StreamingSaveFormat.ChunkPayloadHeaderSize + i * StreamingSaveFormat.ChunkSectionDescSize;
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
        int dataOffset = StreamingSaveFormat.ChunkPayloadHeaderSize + _sectionCount * StreamingSaveFormat.ChunkSectionDescSize;
        for (int i = 0; i < _sectionCount; i++)
        {
            int descOffset = StreamingSaveFormat.ChunkPayloadHeaderSize + i * StreamingSaveFormat.ChunkSectionDescSize;
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
    /// Reads the section at <paramref name="index"/> in header order.
    /// </summary>
    /// <param name="index">The zero-based section index.</param>
    /// <param name="sectionId">The section identifier.</param>
    /// <param name="flags">The section flags.</param>
    /// <param name="data">The section payload slice.</param>
    public void GetSection(int index, out ushort sectionId, out ushort flags, out ReadOnlySpan<byte> data)
    {
        if ((uint)index >= (uint)_sectionCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        int dataOffset = StreamingSaveFormat.ChunkPayloadHeaderSize + _sectionCount * StreamingSaveFormat.ChunkSectionDescSize;
        for (int i = 0; i <= index; i++)
        {
            int descOffset = StreamingSaveFormat.ChunkPayloadHeaderSize + i * StreamingSaveFormat.ChunkSectionDescSize;
            ushort currentId = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(descOffset, 2));
            ushort currentFlags = BinaryPrimitives.ReadUInt16LittleEndian(_payload.Slice(descOffset + 2, 2));
            int byteLength = (int)BinaryPrimitives.ReadUInt32LittleEndian(_payload.Slice(descOffset + 4, 4));

            if (i == index)
            {
                sectionId = currentId;
                flags = currentFlags;
                data = _payload.Slice(dataOffset, byteLength);
                return;
            }

            dataOffset += byteLength;
        }

        throw new ArgumentOutOfRangeException(nameof(index));
    }
}
