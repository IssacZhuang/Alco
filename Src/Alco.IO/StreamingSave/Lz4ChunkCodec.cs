using System;
using K4os.Compression.LZ4;

namespace Alco.IO;

/// <summary>
/// Fast LZ4 codec (<see cref="LZ4Level.L00_FAST"/>) used for hot streaming writes and spill files.
/// </summary>
public sealed class Lz4ChunkCodec : IChunkCodec
{
    /// <inheritdoc/>
    public ChunkCodecId Id => ChunkCodecId.Lz4;

    /// <inheritdoc/>
    public int GetMaxCompressedLength(int uncompressedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uncompressedLength);
        return uncompressedLength == 0 ? 0 : LZ4Codec.MaximumOutputSize(uncompressedLength);
    }

    /// <inheritdoc/>
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.IsEmpty)
            return 0;

        int written = LZ4Codec.Encode(source, destination, LZ4Level.L00_FAST);
        if (written <= 0)
            throw new ArgumentException("Destination buffer is too small for LZ4 compression.", nameof(destination));

        return written;
    }

    /// <inheritdoc/>
    public bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        if (destination.IsEmpty)
        {
            bytesWritten = 0;
            return source.IsEmpty;
        }

        if (source.IsEmpty)
        {
            bytesWritten = 0;
            return false;
        }

        try
        {
            int decoded = LZ4Codec.Decode(source, destination);
            if (decoded != destination.Length)
            {
                bytesWritten = 0;
                return false;
            }

            bytesWritten = decoded;
            return true;
        }
        catch (ArgumentException)
        {
            bytesWritten = 0;
            return false;
        }
    }
}
