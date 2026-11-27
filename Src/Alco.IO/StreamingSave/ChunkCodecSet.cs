using System;
using System.Diagnostics.CodeAnalysis;

namespace Alco.IO;

/// <summary>
/// Owns and dispatches the standard <see cref="ChunkCodecId"/> codec implementations.
/// </summary>
public sealed class ChunkCodecSet : IDisposable
{
    private readonly NoneChunkCodec _none = new();
    private readonly Lz4ChunkCodec _lz4 = new();
    private readonly ZstdChunkCodec _zstd;

    /// <summary>
    /// Initializes a new <see cref="ChunkCodecSet"/>.
    /// </summary>
    /// <param name="zstdCompressionLevel">The Zstandard compression level for <see cref="ChunkCodecId.Zstd"/>.</param>
    public ChunkCodecSet(int zstdCompressionLevel = 3)
    {
        _zstd = new ZstdChunkCodec(zstdCompressionLevel);
    }

    /// <summary>Gets the uncompressed pass-through codec.</summary>
    public NoneChunkCodec None => _none;

    /// <summary>Gets the fast LZ4 codec.</summary>
    public Lz4ChunkCodec Lz4 => _lz4;

    /// <summary>Gets the Zstandard codec.</summary>
    public ZstdChunkCodec Zstd => _zstd;

    /// <summary>
    /// Attempts to resolve the codec implementation for <paramref name="codecId"/>.
    /// </summary>
    /// <param name="codecId">The codec identifier.</param>
    /// <param name="codec">The resolved codec instance when valid.</param>
    /// <returns><see langword="true"/> if <paramref name="codecId"/> is recognized; otherwise, <see langword="false"/>.</returns>
    public bool TryGetCodec(ChunkCodecId codecId, [NotNullWhen(true)] out IChunkCodec? codec)
    {
        switch (codecId)
        {
            case ChunkCodecId.None:
                codec = _none;
                return true;
            case ChunkCodecId.Lz4:
                codec = _lz4;
                return true;
            case ChunkCodecId.Zstd:
                codec = _zstd;
                return true;
            default:
                codec = null;
                return false;
        }
    }

    /// <summary>
    /// Returns the codec implementation for <paramref name="codecId"/> or throws if unsupported.
    /// </summary>
    /// <param name="codecId">The codec identifier.</param>
    /// <returns>The matching codec instance.</returns>
    public IChunkCodec GetCodec(ChunkCodecId codecId)
    {
        if (TryGetCodec(codecId, out IChunkCodec? codec))
            return codec;

        throw new ArgumentOutOfRangeException(nameof(codecId), $"Unsupported chunk codec {(byte)codecId}.");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _zstd.Dispose();
    }
}
