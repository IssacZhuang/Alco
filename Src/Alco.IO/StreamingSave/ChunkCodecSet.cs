using System;
using System.Collections.Concurrent;
using K4os.Compression.LZ4;
using ZstdSharp;

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
/// Thread-safe compression and decompression dispatcher owning a bounded pool of Zstandard native contexts.
/// </summary>
public sealed class ChunkCodecSet : IDisposable
{
    private readonly int _compressionLevel;
    private readonly int _maxPoolSize;
    private readonly ConcurrentBag<Compressor> _compressors = new();
    private readonly ConcurrentBag<Decompressor> _decompressors = new();
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="ChunkCodecSet"/> with the requested Zstandard compression level.
    /// </summary>
    /// <param name="zstdCompressionLevel">The Zstandard compression level (default <c>3</c>).</param>
    public ChunkCodecSet(int zstdCompressionLevel = 3)
    {
        if (zstdCompressionLevel < Compressor.MinCompressionLevel || zstdCompressionLevel > Compressor.MaxCompressionLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(zstdCompressionLevel),
                $"Compression level must be between {Compressor.MinCompressionLevel} and {Compressor.MaxCompressionLevel}.");
        }

        _compressionLevel = zstdCompressionLevel;
        _maxPoolSize = Math.Max(4, Environment.ProcessorCount * 2);
    }

    /// <summary>
    /// Gets the configured Zstandard compression level.
    /// </summary>
    public int ZstdCompressionLevel => _compressionLevel;

    /// <summary>
    /// Returns whether <paramref name="codecId"/> is a supported <see cref="ChunkCodecId"/>.
    /// </summary>
    /// <param name="codecId">The codec identifier.</param>
    /// <returns><see langword="true"/> if supported; otherwise, <see langword="false"/>.</returns>
    public static bool IsValid(ChunkCodecId codecId) =>
        codecId is ChunkCodecId.None or ChunkCodecId.Lz4 or ChunkCodecId.Zstd;

    /// <summary>
    /// Returns the worst-case compressed buffer size required for <paramref name="uncompressedLength"/> bytes.
    /// </summary>
    /// <param name="codecId">The target compression codec.</param>
    /// <param name="uncompressedLength">The uncompressed payload size in bytes.</param>
    /// <returns>The minimum destination buffer capacity in bytes.</returns>
    public int GetMaxCompressedLength(ChunkCodecId codecId, int uncompressedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uncompressedLength);
        return codecId switch
        {
            ChunkCodecId.None => uncompressedLength,
            ChunkCodecId.Lz4 => uncompressedLength == 0 ? 0 : LZ4Codec.MaximumOutputSize(uncompressedLength),
            ChunkCodecId.Zstd => Compressor.GetCompressBound(uncompressedLength),
            _ => throw new ArgumentOutOfRangeException(nameof(codecId), $"Unsupported chunk codec {(byte)codecId}.")
        };
    }

    /// <summary>
    /// Compresses <paramref name="source"/> into <paramref name="destination"/> using <paramref name="codecId"/>.
    /// </summary>
    /// <param name="codecId">The compression codec to apply.</param>
    /// <param name="source">The uncompressed input bytes.</param>
    /// <param name="destination">The output buffer sized at least <see cref="GetMaxCompressedLength"/>.</param>
    /// <returns>The number of compressed bytes written to <paramref name="destination"/>.</returns>
    public int Compress(ChunkCodecId codecId, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return codecId switch
        {
            ChunkCodecId.None => CompressNone(source, destination),
            ChunkCodecId.Lz4 => CompressLz4(source, destination),
            ChunkCodecId.Zstd => CompressZstd(source, destination),
            _ => throw new ArgumentOutOfRangeException(nameof(codecId), $"Unsupported chunk codec {(byte)codecId}.")
        };
    }

    /// <summary>
    /// Attempts to decompress <paramref name="source"/> into <paramref name="destination"/> using <paramref name="codecId"/>.
    /// </summary>
    /// <param name="codecId">The compression codec recorded for the payload.</param>
    /// <param name="source">The compressed input bytes.</param>
    /// <param name="destination">The destination buffer sized to the expected uncompressed length.</param>
    /// <param name="bytesWritten">The number of decompressed bytes written on success.</param>
    /// <returns><see langword="true"/> if decompression succeeded and matched <paramref name="destination"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryDecompress(ChunkCodecId codecId, ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        switch (codecId)
        {
            case ChunkCodecId.None:
                return TryDecompressNone(source, destination, out bytesWritten);
            case ChunkCodecId.Lz4:
                return TryDecompressLz4(source, destination, out bytesWritten);
            case ChunkCodecId.Zstd:
                return TryDecompressZstd(source, destination, out bytesWritten);
            default:
                bytesWritten = 0;
                return false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        while (_compressors.TryTake(out Compressor? compressor))
            compressor.Dispose();

        while (_decompressors.TryTake(out Decompressor? decompressor))
            decompressor.Dispose();
    }

    private static int CompressNone(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (destination.Length < source.Length)
            throw new ArgumentException("Destination buffer is smaller than source payload.", nameof(destination));

        source.CopyTo(destination);
        return source.Length;
    }

    private static bool TryDecompressNone(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
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

    private static int CompressLz4(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (source.IsEmpty)
            return 0;

        int written = LZ4Codec.Encode(source, destination, LZ4Level.L00_FAST);
        if (written <= 0)
            throw new ArgumentException("Destination buffer is too small for LZ4 compression.", nameof(destination));

        return written;
    }

    private static bool TryDecompressLz4(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
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

    private int CompressZstd(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        Compressor compressor = RentCompressor();
        try
        {
            if (!compressor.TryWrap(source, destination, out int written))
                throw new ArgumentException("Destination buffer is too small for Zstd compression.", nameof(destination));

            return written;
        }
        finally
        {
            ReturnCompressor(compressor);
        }
    }

    private bool TryDecompressZstd(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        if (source.IsEmpty)
        {
            bytesWritten = 0;
            return false;
        }

        Decompressor decompressor = RentDecompressor();
        try
        {
            if (!decompressor.TryUnwrap(source, destination, out int written) || written != destination.Length)
            {
                bytesWritten = 0;
                return false;
            }

            bytesWritten = written;
            return true;
        }
        catch (ZstdException)
        {
            bytesWritten = 0;
            return false;
        }
        catch (ArgumentException)
        {
            bytesWritten = 0;
            return false;
        }
        finally
        {
            ReturnDecompressor(decompressor);
        }
    }

    private Compressor RentCompressor() =>
        _compressors.TryTake(out Compressor? compressor) ? compressor : new Compressor(_compressionLevel);

    private void ReturnCompressor(Compressor compressor)
    {
        if (_disposed || _compressors.Count >= _maxPoolSize)
        {
            compressor.Dispose();
            return;
        }

        _compressors.Add(compressor);
    }

    private Decompressor RentDecompressor() =>
        _decompressors.TryTake(out Decompressor? decompressor) ? decompressor : new Decompressor();

    private void ReturnDecompressor(Decompressor decompressor)
    {
        if (_disposed || _decompressors.Count >= _maxPoolSize)
        {
            decompressor.Dispose();
            return;
        }

        _decompressors.Add(decompressor);
    }
}
