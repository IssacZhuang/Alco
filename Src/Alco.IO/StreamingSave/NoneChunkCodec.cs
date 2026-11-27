using System;

namespace Alco.IO;

/// <summary>
/// Pass-through codec that stores chunk payloads without compression.
/// </summary>
public sealed class NoneChunkCodec : IChunkCodec
{
    /// <inheritdoc/>
    public ChunkCodecId Id => ChunkCodecId.None;

    /// <inheritdoc/>
    public int GetMaxCompressedLength(int uncompressedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uncompressedLength);
        return uncompressedLength;
    }

    /// <inheritdoc/>
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length < source.Length)
            throw new ArgumentException("Destination buffer is smaller than source payload.", nameof(destination));

        source.CopyTo(destination);
        return source.Length;
    }

    /// <inheritdoc/>
    public bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        if (source.Length != destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        source.CopyTo(destination);
        bytesWritten = source.Length;
        return true;
    }
}
