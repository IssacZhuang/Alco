using System.IO;
using System.IO.Hashing;
using Alco;
using Alco.IO;

namespace Alco.World3D;

/// <summary>
/// Reads a mesh asset package (.amsh) over a seekable stream with positional, concurrency-safe
/// access (backed by <see cref="PackageReader{TMeta}"/>). Owns the stream. Hash-verifies chunk
/// reads against the meta descriptors. Reading counterpart of <see cref="MeshAssetWriter"/>;
/// supports codec <see cref="MeshChunkCodec.None"/> only.
/// </summary>
public sealed unsafe class MeshAssetReader : AutoDisposable
{
    private readonly Stream _stream;
    private readonly PackageReader<MeshAssetMeta> _reader;

    /// <summary>Gets the parsed meta of the file.</summary>
    public MeshAssetMeta Meta => _reader.Meta;

    private MeshAssetReader(Stream stream, PackageReader<MeshAssetMeta> reader)
    {
        _stream = stream;
        _reader = reader;
    }

    /// <summary>
    /// Open a reader over a seekable stream. The reader takes ownership of the stream.
    /// </summary>
    /// <param name="stream">The seekable package stream positioned at offset 0.</param>
    /// <param name="name">The asset name used in diagnostics.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="InvalidDataException">Thrown when the magic or version is invalid.</exception>
    public static MeshAssetReader Open(Stream stream, string name)
    {
        ArgumentNullException.ThrowIfNull(stream);

        PackageReader<MeshAssetMeta> reader = PackageReader<MeshAssetMeta>.OpenStream(stream);
        MeshAssetFormatVersion.Validate(reader.Meta.Version);
        return new MeshAssetReader(stream, reader);
    }

    /// <summary>
    /// Open a reader over a mesh asset held fully in memory (e.g. from a preloaded asset context).
    /// The buffer is copied and owned by the reader.
    /// </summary>
    /// <param name="data">The mesh asset bytes.</param>
    /// <returns>The reader.</returns>
    /// <exception cref="InvalidDataException">Thrown when the magic or version is invalid.</exception>
    public static MeshAssetReader OpenMemory(ReadOnlySpan<byte> data)
    {
        byte[] copy = data.ToArray();
        PackageReader<MeshAssetMeta> reader = PackageReader<MeshAssetMeta>.OpenMemory(copy);
        MeshAssetFormatVersion.Validate(reader.Meta.Version);
        return new MeshAssetReader(new MemoryStream(copy, writable: false), reader);
    }

    /// <summary>
    /// Try to get the stored size of a content entry.
    /// </summary>
    /// <param name="entryName">The entry name.</param>
    /// <param name="size">The entry size in bytes when found.</param>
    /// <returns>True when the entry exists.</returns>
    public bool TryGetEntrySize(string entryName, out long size)
    {
        if (_reader.TryGetEntry(entryName, out PackageEntry? entry))
        {
            size = entry.Size;
            return true;
        }

        size = 0;
        return false;
    }

    /// <summary>
    /// Read a whole content entry. The destination length must equal the entry size.
    /// </summary>
    /// <param name="entryName">The entry name.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <exception cref="InvalidDataException">Thrown when the entry does not exist.</exception>
    public void ReadEntry(string entryName, Span<byte> destination)
    {
        if (!_reader.TryGetEntry(entryName, out PackageEntry? entry))
        {
            throw new InvalidDataException($"Mesh asset entry '{entryName}' does not exist.");
        }

        _reader.ReadByEntry(entry, destination);
    }

    /// <summary>
    /// Read a sub-range of a content entry.
    /// </summary>
    /// <param name="entryName">The entry name.</param>
    /// <param name="entryOffset">Byte offset inside the entry.</param>
    /// <param name="destination">The destination buffer; fewer bytes than requested may arrive at the entry end.</param>
    /// <returns>The number of bytes read.</returns>
    /// <exception cref="InvalidDataException">Thrown when the entry does not exist.</exception>
    public int ReadEntryRange(string entryName, long entryOffset, Span<byte> destination)
    {
        if (!_reader.TryGetEntry(entryName, out PackageEntry? entry))
        {
            throw new InvalidDataException($"Mesh asset entry '{entryName}' does not exist.");
        }

        return _reader.ReadByEntry(entry, destination, entryOffset);
    }

    /// <summary>
    /// Read a typed chunk into a pre-allocated buffer and verify it: the codec must be
    /// <see cref="MeshChunkCodec.None"/>, the destination must match the stored size and the
    /// xxHash64 of the read bytes must equal the descriptor hash.
    /// </summary>
    /// <param name="chunk">The chunk descriptor.</param>
    /// <param name="destination">The destination buffer sized to the chunk's stored size.</param>
    /// <exception cref="InvalidDataException">Thrown on missing entry, size mismatch or hash mismatch.</exception>
    /// <exception cref="NotSupportedException">Thrown for codecs other than None.</exception>
    public void ReadChunk(in MeshChunkMeta chunk, SafeMemoryHandle destination)
    {
        if (chunk.Codec != MeshChunkCodec.None)
        {
            throw new NotSupportedException($"Mesh asset chunk '{chunk.Entry}' uses unsupported codec {chunk.Codec}.");
        }

        if (!TryGetEntrySize(chunk.Entry, out long size))
        {
            throw new InvalidDataException($"Mesh asset chunk '{chunk.Entry}' does not exist.");
        }

        if (size != destination.AsReadOnlySpan().Length)
        {
            throw new InvalidDataException(
                $"Mesh asset chunk '{chunk.Entry}' size mismatch: expected {destination.AsReadOnlySpan().Length}, stored {size}.");
        }

        ReadEntry(chunk.Entry, destination.AsSpan());

        ulong hash = XxHash64.HashToUInt64(destination.AsReadOnlySpan());
        if (hash != chunk.Hash)
        {
            throw new InvalidDataException(
                $"Mesh asset chunk '{chunk.Entry}' hash mismatch: expected {chunk.Hash}, computed {hash}.");
        }
    }

    /// <summary>
    /// Find the typed chunk descriptor for an entry name.
    /// </summary>
    /// <param name="entryName">The entry name.</param>
    /// <returns>The descriptor.</returns>
    /// <exception cref="InvalidDataException">Thrown when no descriptor references the entry.</exception>
    public MeshChunkMeta GetChunk(string entryName)
    {
        foreach (MeshChunkMeta chunk in Meta.Chunks)
        {
            if (chunk.Entry == entryName)
            {
                return chunk;
            }
        }

        throw new InvalidDataException($"Mesh asset entry '{entryName}' has no chunk descriptor.");
    }

    /// <summary>
    /// Disposes the package reader and the underlying stream. Pure IO — safe from any thread,
    /// including the finalizer path.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>, false from the finalizer.</param>
    protected override void Dispose(bool disposing)
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}
