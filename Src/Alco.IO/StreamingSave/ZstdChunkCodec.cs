using System;
using System.Collections.Concurrent;
using ZstdSharp;

namespace Alco.IO;

/// <summary>
/// Thread-safe Zstandard codec backed by a bounded instance-owned pool of compressors and decompressors.
/// </summary>
public sealed class ZstdChunkCodec : IChunkCodec, IDisposable
{
    private readonly int _compressionLevel;
    private readonly int _maxPoolSize;
    private readonly ConcurrentBag<Compressor> _compressors = new();
    private readonly ConcurrentBag<Decompressor> _decompressors = new();
    private volatile bool _disposed;

    /// <summary>
    /// Initializes a new <see cref="ZstdChunkCodec"/> at the requested compression level.
    /// </summary>
    /// <param name="compressionLevel">The Zstandard compression level (default <c>3</c>).</param>
    public ZstdChunkCodec(int compressionLevel = 3)
    {
        if (compressionLevel < Compressor.MinCompressionLevel || compressionLevel > Compressor.MaxCompressionLevel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(compressionLevel),
                $"Compression level must be between {Compressor.MinCompressionLevel} and {Compressor.MaxCompressionLevel}.");
        }

        _compressionLevel = compressionLevel;
        _maxPoolSize = Math.Max(4, Environment.ProcessorCount * 2);
    }

    /// <inheritdoc/>
    public ChunkCodecId Id => ChunkCodecId.Zstd;

    /// <summary>
    /// Gets the configured Zstandard compression level.
    /// </summary>
    public int CompressionLevel => _compressionLevel;

    /// <inheritdoc/>
    public int GetMaxCompressedLength(int uncompressedLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(uncompressedLength);
        return Compressor.GetCompressBound(uncompressedLength);
    }

    /// <inheritdoc/>
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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

    /// <inheritdoc/>
    public bool TryDecompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

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
