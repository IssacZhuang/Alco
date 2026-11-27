using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;

namespace Alco.IO;

[Flags]
internal enum SegmentFlags : ushort
{
    None = 0,
    Spill = 1 << 0,
    Compacted = 1 << 1
}

internal static class ChunkRecordCodec
{
    public const int SegmentHeaderSize = 64;
    public const ushort SegmentFormatVersion = 1;

    public const int ChunkRecordHeaderSize = 32;
    public const int ChunkRecordTrailerSize = 8;
    public const int RecordAlignment = 16;
    public const byte DefaultChunkSchemaVersion = 1;

    public const int ChunkPayloadHeaderSize = 16;
    public const ushort ChunkPayloadVersion = 1;
    public const int ChunkSectionDescSize = 8;

    public const long DefaultMaxSegmentBytes = 32L * 1024 * 1024;
    public const int DefaultMaxUncompressedChunkBytes = 64 * 1024 * 1024;

    public static ReadOnlySpan<byte> SegmentMagicBytes => "ACSG"u8;
    public static ReadOnlySpan<byte> ChunkRecordMagicBytes => "ACRC"u8;
    public static ReadOnlySpan<byte> ChunkPayloadMagicBytes => "ACHK"u8;

    public static long Align16(long value) => (value + (RecordAlignment - 1L)) & ~(RecordAlignment - 1L);

    public static int Align16(int value) => (value + (RecordAlignment - 1)) & ~(RecordAlignment - 1);

    public static long AlignedChunkRecordLength(uint compressedSize) =>
        Align16((long)ChunkRecordHeaderSize + compressedSize + ChunkRecordTrailerSize);

    public static void WriteGuidRfc4122(Span<byte> destination, Guid value)
    {
        if (!value.TryWriteBytes(destination, bigEndian: true, out int written) || written != 16)
            throw new InvalidOperationException("Failed to write 16-byte RFC 4122 GUID.");
    }

    public static Guid ReadGuidRfc4122(ReadOnlySpan<byte> source) =>
        new(source[..16], bigEndian: true);

    public static void WriteSegmentHeader(
        Span<byte> destination,
        SegmentFlags flags,
        uint segmentId,
        Guid worldGuid,
        long createdUtcTicks)
    {
        destination[..SegmentHeaderSize].Clear();
        SegmentMagicBytes.CopyTo(destination);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), SegmentFormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), (ushort)flags);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8, 4), segmentId);
        // Reserved0 [12..16] remains 0.
        WriteGuidRfc4122(destination.Slice(16, 16), worldGuid);
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(32, 8), createdUtcTicks);
        // Reserved1 [40..64] remains 0.
    }

    public static bool TryReadAndValidateSegmentHeader(
        IRandomAccessHandle handle,
        Guid expectedWorldGuid,
        uint expectedSegmentId,
        bool expectedSpill,
        out SegmentFlags flags,
        [NotNullWhen(false)] out string? failureReason)
    {
        flags = SegmentFlags.None;
        if (handle.Length < SegmentHeaderSize)
        {
            failureReason = "Segment file is shorter than 64-byte header.";
            return false;
        }

        Span<byte> header = stackalloc byte[SegmentHeaderSize];
        if (handle.ReadAt(0, header) != SegmentHeaderSize)
        {
            failureReason = "Failed to read 64-byte segment header.";
            return false;
        }

        if (!header[..4].SequenceEqual(SegmentMagicBytes))
        {
            failureReason = "Invalid segment magic header.";
            return false;
        }

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(4, 2));
        if (version != SegmentFormatVersion)
        {
            failureReason = $"Unsupported segment version {version}.";
            return false;
        }

        ushort rawFlags = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(6, 2));
        if ((rawFlags & ~(ushort)(SegmentFlags.Spill | SegmentFlags.Compacted)) != 0)
        {
            failureReason = $"Unknown segment flags 0x{rawFlags:X4}.";
            return false;
        }

        flags = (SegmentFlags)rawFlags;
        bool isSpill = (flags & SegmentFlags.Spill) != 0;
        if (isSpill != expectedSpill)
        {
            failureReason = expectedSpill ? "Expected spill segment flag." : "Committed segment unexpectedly marked as spill.";
            return false;
        }

        uint segmentId = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));
        if (segmentId != expectedSegmentId)
        {
            failureReason = $"Segment ID mismatch: expected {expectedSegmentId}, got {segmentId}.";
            return false;
        }

        uint reserved0 = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(12, 4));
        if (reserved0 != 0)
        {
            failureReason = "Corrupt segment header reserved field.";
            return false;
        }

        Guid worldGuid = ReadGuidRfc4122(header.Slice(16, 16));
        if (worldGuid != expectedWorldGuid)
        {
            failureReason = $"Segment world GUID mismatch: expected {expectedWorldGuid:N}, got {worldGuid:N}.";
            return false;
        }

        failureReason = null;
        return true;
    }

    public static int WriteChunkRecord(
        Span<byte> destination,
        in ChunkIndexEntry entry,
        ReadOnlySpan<byte> compressedPayload)
    {
        if (compressedPayload.Length != entry.CompressedSize)
            throw new ArgumentException("Compressed payload length does not match index entry.", nameof(compressedPayload));

        int crcInputLength = ChunkRecordHeaderSize + compressedPayload.Length;
        int totalRecordLength = crcInputLength + ChunkRecordTrailerSize;
        int alignedLength = Align16(totalRecordLength);

        destination[..ChunkRecordHeaderSize].Clear();
        ChunkRecordMagicBytes.CopyTo(destination);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4, 4), entry.MapId);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(8, 4), entry.ChunkX);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(12, 4), entry.ChunkY);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(16, 4), entry.CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(20, 4), entry.UncompressedSize);
        destination[24] = (byte)entry.Codec;
        destination[25] = entry.SchemaVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(26, 2), entry.Flags);
        // Reserved [28..32] remains 0.

        if (!compressedPayload.IsEmpty)
            compressedPayload.CopyTo(destination.Slice(ChunkRecordHeaderSize, compressedPayload.Length));

        ulong crc = XxHash64.HashToUInt64(destination[..crcInputLength]);
        BinaryPrimitives.WriteUInt64LittleEndian(destination.Slice(crcInputLength, ChunkRecordTrailerSize), crc);

        if (alignedLength > totalRecordLength)
            destination[totalRecordLength..alignedLength].Clear();

        return alignedLength;
    }

    public static bool TryReadVerifiedCompressedRecord(
        IRandomAccessHandle handle,
        long fileOffset,
        in ChunkIndexEntry expected,
        IBufferWriter<byte> compressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        if (!TryReadVerifiedRecordBuffer(handle, fileOffset, expected, out byte[]? rentedBuffer, out int compressedLength, out failureReason))
            return false;

        try
        {
            if (compressedLength > 0)
            {
                Span<byte> dst = compressedDestination.GetSpan(compressedLength)[..compressedLength];
                rentedBuffer.AsSpan(ChunkRecordHeaderSize, compressedLength).CopyTo(dst);
                compressedDestination.Advance(compressedLength);
            }

            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    public static bool TryReadAndDecompressChunkRecord(
        IRandomAccessHandle handle,
        long fileOffset,
        in ChunkIndexEntry expected,
        ChunkCodecSet codecs,
        int maxUncompressedBytes,
        IBufferWriter<byte> uncompressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        if (!ChunkCodecSet.IsValid(expected.Codec))
        {
            failureReason = $"Unknown chunk codec ID {(byte)expected.Codec}.";
            return false;
        }

        if (expected.UncompressedSize > (uint)maxUncompressedBytes)
        {
            failureReason = $"Chunk uncompressed size {expected.UncompressedSize} exceeds limit {maxUncompressedBytes}.";
            return false;
        }

        if (!TryReadVerifiedRecordBuffer(handle, fileOffset, expected, out byte[]? rentedBuffer, out int compressedLength, out failureReason))
            return false;

        try
        {
            ReadOnlySpan<byte> compressedSpan = rentedBuffer.AsSpan(ChunkRecordHeaderSize, compressedLength);
            int uncompressedLength = (int)expected.UncompressedSize;
            if (uncompressedLength == 0)
            {
                if (!codecs.TryDecompress(expected.Codec, compressedSpan, Span<byte>.Empty, out int written) || written != 0)
                {
                    failureReason = $"Failed to decompress 0-byte chunk {expected.Key} with codec {expected.Codec}.";
                    return false;
                }

                return true;
            }

            Span<byte> destinationSpan = uncompressedDestination.GetSpan(uncompressedLength)[..uncompressedLength];
            if (!codecs.TryDecompress(expected.Codec, compressedSpan, destinationSpan, out int bytesWritten) || bytesWritten != uncompressedLength)
            {
                failureReason = $"Failed to decompress chunk {expected.Key} with codec {expected.Codec} (expected {uncompressedLength} bytes, got {bytesWritten}).";
                return false;
            }

            uncompressedDestination.Advance(bytesWritten);
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    public static bool TryReadForTargetCodec(
        IChunkReader reader,
        in ChunkIndexEntry entry,
        ChunkCodecId targetCodec,
        ChunkCodecSet codecs,
        ArrayBufferWriter<byte> uncompressedScratch,
        IBufferWriter<byte> compressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        if (entry.Codec == targetCodec)
            return reader.TryReadCompressedChunk(entry, compressedDestination, out failureReason);

        uncompressedScratch.Clear();
        if (!reader.TryReadChunk(entry, uncompressedScratch, out failureReason))
            return false;

        CompressIntoWriter(uncompressedScratch.WrittenSpan, targetCodec, codecs, compressedDestination);
        return true;
    }

    public static bool TryReadForTargetCodec(
        IRandomAccessHandle handle,
        long fileOffset,
        in ChunkIndexEntry entry,
        ChunkCodecId targetCodec,
        ChunkCodecSet codecs,
        int maxUncompressedBytes,
        ArrayBufferWriter<byte> uncompressedScratch,
        IBufferWriter<byte> compressedDestination,
        [NotNullWhen(false)] out string? failureReason)
    {
        if (entry.Codec == targetCodec)
            return TryReadVerifiedCompressedRecord(handle, fileOffset, entry, compressedDestination, out failureReason);

        uncompressedScratch.Clear();
        if (!TryReadAndDecompressChunkRecord(handle, fileOffset, entry, codecs, maxUncompressedBytes, uncompressedScratch, out failureReason))
            return false;

        CompressIntoWriter(uncompressedScratch.WrittenSpan, targetCodec, codecs, compressedDestination);
        return true;
    }

    private static void CompressIntoWriter(
        ReadOnlySpan<byte> uncompressed,
        ChunkCodecId targetCodec,
        ChunkCodecSet codecs,
        IBufferWriter<byte> compressedDestination)
    {
        int maxCompressed = codecs.GetMaxCompressedLength(targetCodec, uncompressed.Length);
        if (maxCompressed == 0)
            return;

        Span<byte> dst = compressedDestination.GetSpan(maxCompressed)[..maxCompressed];
        int written = codecs.Compress(targetCodec, uncompressed, dst);
        compressedDestination.Advance(written);
    }

    private static bool TryReadVerifiedRecordBuffer(
        IRandomAccessHandle handle,
        long fileOffset,
        in ChunkIndexEntry expected,
        [NotNullWhen(true)] out byte[]? rentedBuffer,
        out int compressedLength,
        [NotNullWhen(false)] out string? failureReason)
    {
        rentedBuffer = null;
        compressedLength = 0;

        if (fileOffset < 0 || expected.CompressedSize > int.MaxValue - ChunkRecordHeaderSize - ChunkRecordTrailerSize)
        {
            failureReason = $"Chunk {expected.Key} offset or compressed size is out of bounds.";
            return false;
        }

        long crcInputLengthLong = ChunkRecordHeaderSize + (long)expected.CompressedSize;
        long totalRecordBytesLong = crcInputLengthLong + ChunkRecordTrailerSize;
        if (fileOffset + totalRecordBytesLong > handle.Length)
        {
            failureReason = $"Chunk {expected.Key} record at offset {fileOffset} exceeds file length {handle.Length}.";
            return false;
        }

        int crcInputLength = (int)crcInputLengthLong;
        int totalRecordBytes = (int)totalRecordBytesLong;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(totalRecordBytes);
        bool keepBuffer = false;
        try
        {
            Span<byte> recordSpan = buffer.AsSpan(0, totalRecordBytes);
            int bytesRead = handle.ReadAt(fileOffset, recordSpan);
            if (bytesRead != totalRecordBytes)
            {
                failureReason = $"Short read for chunk {expected.Key} at offset {fileOffset}: expected {totalRecordBytes}, got {bytesRead}.";
                return false;
            }

            ulong expectedCrc = BinaryPrimitives.ReadUInt64LittleEndian(recordSpan.Slice(crcInputLength, ChunkRecordTrailerSize));
            ulong actualCrc = XxHash64.HashToUInt64(recordSpan[..crcInputLength]);
            if (expectedCrc != actualCrc)
            {
                failureReason = $"Chunk {expected.Key} XxHash64 mismatch at offset {fileOffset}.";
                return false;
            }

            if (!recordSpan[..4].SequenceEqual(ChunkRecordMagicBytes))
            {
                failureReason = $"Invalid chunk record magic at offset {fileOffset}.";
                return false;
            }

            uint mapId = BinaryPrimitives.ReadUInt32LittleEndian(recordSpan.Slice(4, 4));
            int chunkX = BinaryPrimitives.ReadInt32LittleEndian(recordSpan.Slice(8, 4));
            int chunkY = BinaryPrimitives.ReadInt32LittleEndian(recordSpan.Slice(12, 4));
            uint compSize = BinaryPrimitives.ReadUInt32LittleEndian(recordSpan.Slice(16, 4));
            uint uncompSize = BinaryPrimitives.ReadUInt32LittleEndian(recordSpan.Slice(20, 4));
            byte codec = recordSpan[24];
            byte schemaVersion = recordSpan[25];
            ushort rawFlags = BinaryPrimitives.ReadUInt16LittleEndian(recordSpan.Slice(26, 2));
            uint reserved = BinaryPrimitives.ReadUInt32LittleEndian(recordSpan.Slice(28, 4));

            if (mapId != expected.MapId ||
                chunkX != expected.ChunkX ||
                chunkY != expected.ChunkY ||
                compSize != expected.CompressedSize ||
                uncompSize != expected.UncompressedSize ||
                codec != (byte)expected.Codec ||
                schemaVersion != expected.SchemaVersion ||
                rawFlags != expected.Flags ||
                reserved != 0)
            {
                failureReason = $"Chunk record header metadata mismatch at offset {fileOffset} for {expected.Key}.";
                return false;
            }

            compressedLength = (int)expected.CompressedSize;
            rentedBuffer = buffer;
            keepBuffer = true;
            failureReason = null;
            return true;
        }
        finally
        {
            if (!keepBuffer)
                ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
