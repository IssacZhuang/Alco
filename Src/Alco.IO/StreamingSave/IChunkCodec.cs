using System;

namespace Alco.IO;

/// <summary>
/// Compression codec identifier stored in chunk and snapshot headers.
/// </summary>
public enum ChunkCodecId : byte
{
    /// <summary>Uncompressed raw payload bytes.</summary>
    None = 0,

    /// <summary>Fast LZ4 compression (<c>LZ4Level.L00_FAST</c>) for hot streaming and spill paths.</summary>
    Lz4 = 1,

    /// <summary>Zstandard compression (default level 3) for compacted segments and portable archives.</summary>
    Zstd = 2
}

/// <summary>
/// Stateless or thread-safe compressor and decompressor for chunk and core snapshot payloads.
/// </summary>
public interface IChunkCodec
{
    /// <summary>
    /// Gets the codec identifier written to binary headers.
    /// </summary>
    ChunkCodecId Id { get; }

    /// <summary>
    /// Returns the worst-case compressed buffer size required for <paramref name="uncompressedLength"/> bytes.
    /// </summary>
    /// <param name="uncompressedLength">The uncompressed payload size in bytes.</param>
    /// <returns>The minimum destination buffer capacity in bytes.</returns>
    int GetMaxCompressedLength(int uncompressedLength);

    /// <summary>
    /// Compresses <paramref name="source"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">The uncompressed input bytes.</param>
    /// <param name="destination">The output buffer sized at least <see cref="GetMaxCompressedLength"/>.</param>
    /// <returns>The number of compressed bytes written to <paramref name="destination"/>.</returns>
    int Compress(ReadOnlySpan<byte> source, Span<byte> destination);

    /// <summary>
    /// Attempts to decompress <paramref name="source"/> into <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">The compressed input bytes.</param>
    /// <param name="destination">The destination buffer sized to the expected uncompressed length.</param>
    /// <param name="bytesWritten">The number of decompressed bytes written on success.</param>
    /// <returns><see langword="true"/> if decompression succeeded and matched <paramref name="destination"/>; otherwise, <see langword="false"/>.</returns>
    bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten);
}
