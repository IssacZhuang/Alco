using System.Buffers.Binary;

namespace Alco.IO;

/// <summary>
/// Constants for the Alco package on-disk format: a fixed 64-byte header, followed by append-only
/// entry content, followed by a tail-appended entry directory. The header carries two double-buffered
/// directory descriptors so <see cref="PackageWriter{TMeta}"/> can commit incremental updates by
/// appending data and repointing one descriptor, while <see cref="PackageReader{TMeta}"/> keeps
/// positional per-entry reads identical to a freshly sealed package.
/// </summary>
public static class PackageFormat
{
    /// <summary>Total size of the fixed header prologue in bytes.</summary>
    public const int HeaderSize = 64;

    /// <summary>Header offset of directory descriptor A (the slot written at creation).</summary>
    public const int DescriptorAOffset = 8;

    /// <summary>Header offset of directory descriptor B.</summary>
    public const int DescriptorBOffset = 32;

    /// <summary>Encoded size of one directory descriptor in bytes.</summary>
    public const int DescriptorSize = 24;

    /// <summary>Current on-disk format version (header bytes [4..7], UInt32 little-endian).</summary>
    public const uint Version = 1;

    /// <summary>Sequence number of the directory committed when a package is created.</summary>
    public const uint InitialSequence = 1;

    /// <summary>Gets the header offset of the descriptor at the given index (0 = A, 1 = B).</summary>
    /// <param name="descriptorIndex">Descriptor index, 0 or 1.</param>
    /// <returns>The descriptor's byte offset within the fixed header.</returns>
    public static int DescriptorOffset(int descriptorIndex)
    {
        return descriptorIndex == 0 ? DescriptorAOffset : DescriptorBOffset;
    }
}

/// <summary>
/// One header slot pointing at a committed entry directory: 24 little-endian bytes laid out as
/// [offset Int64][length UInt32][hash UInt64][sequence UInt32]. The sequence is encoded last so a
/// torn in-place patch never promotes a partially written descriptor over the committed one: a
/// partially written slot either keeps the stale sequence (not selected) or fails the bounds/hash
/// validation during parsing.
/// </summary>
internal readonly struct PackageDirectoryDescriptor
{
    /// <summary>A descriptor with sequence 0; never selected by readers.</summary>
    public static readonly PackageDirectoryDescriptor Unused = default;

    /// <summary>Absolute file offset of the directory payload.</summary>
    public long Offset { get; init; }

    /// <summary>Length of the directory payload in bytes.</summary>
    public uint Length { get; init; }

    /// <summary>XxHash64 hash of the directory payload.</summary>
    public ulong Hash { get; init; }

    /// <summary>Commit sequence; strictly increasing, 0 means unused.</summary>
    public uint Sequence { get; init; }

    /// <summary>Gets a value indicating whether this slot was never written.</summary>
    public bool IsUnused => Sequence == 0;

    /// <summary>
    /// Parses a descriptor from a header buffer.
    /// </summary>
    /// <param name="header">The 64-byte package header.</param>
    /// <param name="offset">Byte offset of the descriptor within <paramref name="header"/>.</param>
    /// <returns>The parsed descriptor.</returns>
    public static PackageDirectoryDescriptor Parse(ReadOnlySpan<byte> header, int offset)
    {
        return new PackageDirectoryDescriptor
        {
            Offset = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(offset, 8)),
            Length = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(offset + 8, 4)),
            Hash = BinaryPrimitives.ReadUInt64LittleEndian(header.Slice(offset + 12, 8)),
            Sequence = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(offset + 20, 4)),
        };
    }

    /// <summary>
    /// Encodes this descriptor into a buffer. The sequence is written last (see type remarks).
    /// </summary>
    /// <param name="destination">Destination buffer, at least 24 bytes.</param>
    /// <param name="offset">Byte offset within <paramref name="destination"/>.</param>
    public void Encode(Span<byte> destination, int offset)
    {
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(offset, 8), Offset);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 8, 4), Length);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(offset + 12, 8), Hash);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 20, 4), Sequence);
    }
}

/// <summary>
/// Encodes fixed package headers: [magic 4B][version UInt32][descriptor A 24B][descriptor B 24B]
/// plus zero padding out to <see cref="PackageFormat.HeaderSize"/>.
/// </summary>
internal static class PackageHeader
{
    /// <summary>
    /// Encodes a complete fixed header.
    /// </summary>
    /// <param name="header">Destination buffer of exactly <see cref="PackageFormat.HeaderSize"/> bytes.</param>
    /// <param name="magic">The concrete package type's 4-byte magic.</param>
    /// <param name="a">Descriptor A.</param>
    /// <param name="b">Descriptor B.</param>
    public static void Encode(Span<byte> header, ReadOnlySpan<byte> magic, PackageDirectoryDescriptor a, PackageDirectoryDescriptor b)
    {
        header.Clear();
        magic.CopyTo(header[..4]);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), PackageFormat.Version);
        a.Encode(header, PackageFormat.DescriptorAOffset);
        b.Encode(header, PackageFormat.DescriptorBOffset);
    }
}
