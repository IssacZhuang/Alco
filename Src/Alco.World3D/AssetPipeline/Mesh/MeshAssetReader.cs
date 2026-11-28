using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Alco;
using Alco.IO;

namespace Alco.World3D;

/// <summary>
/// Reads a mesh asset package (.amsh) over a seekable stream with positional access
/// and xxHash64 verification against chunk descriptors.
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
    /// Opens a reader over a seekable stream. Ownership of <paramref name="stream"/> transfers to the reader.
    /// </summary>
    /// <param name="stream">The seekable package stream positioned at offset 0.</param>
    /// <param name="name">The asset name used in diagnostics.</param>
    /// <returns>The opened reader.</returns>
    /// <exception cref="InvalidDataException">Thrown when the magic or version is invalid.</exception>
    public static MeshAssetReader Open(Stream stream, string name)
    {
        ArgumentNullException.ThrowIfNull(stream);
        PackageReader<MeshAssetMeta> reader = PackageReader<MeshAssetMeta>.OpenStream(stream);
        MeshAssetFormatVersion.Validate(reader.Meta.Version);
        return new MeshAssetReader(stream, reader);
    }

    /// <summary>
    /// Opens a reader over a mesh asset held fully in memory.
    /// </summary>
    /// <param name="data">The mesh asset bytes.</param>
    /// <returns>The opened reader.</returns>
    /// <exception cref="InvalidDataException">Thrown when the magic or version is invalid.</exception>
    public static MeshAssetReader OpenMemory(ReadOnlySpan<byte> data)
    {
        byte[] copy = data.ToArray();
        PackageReader<MeshAssetMeta> reader = PackageReader<MeshAssetMeta>.OpenMemory(copy);
        MeshAssetFormatVersion.Validate(reader.Meta.Version);
        return new MeshAssetReader(new MemoryStream(copy, writable: false), reader);
    }

    /// <summary>Tries to locate a package entry descriptor by name.</summary>
    /// <param name="entryName">The entry name.</param>
    /// <param name="entry">Receives the located entry descriptor when found.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
    public bool TryGetEntry(string entryName, [NotNullWhen(true)] out PackageEntry? entry) =>
        _reader.TryGetEntry(entryName, out entry);

    /// <summary>
    /// Tries to get the stored size of a content entry.
    /// </summary>
    /// <param name="entryName">The entry name.</param>
    /// <param name="size">The entry size in bytes when found.</param>
    /// <returns><see langword="true"/> when the entry exists.</returns>
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
    /// Reads a whole content entry into <paramref name="destination"/>.
    /// </summary>
    /// <param name="entryName">The entry name.</param>
    /// <param name="destination">The destination buffer matching the entry size.</param>
    /// <exception cref="InvalidDataException">Thrown when the entry does not exist.</exception>
    public void ReadEntry(string entryName, Span<byte> destination)
    {
        if (!_reader.TryGetEntry(entryName, out PackageEntry? entry))
            throw new InvalidDataException($"Mesh asset entry '{entryName}' does not exist.");
        _reader.ReadByEntry(entry, destination);
    }

    /// <summary>
    /// Reads a sub-range of a content entry.
    /// </summary>
    /// <param name="entryName">The entry name.</param>
    /// <param name="entryOffset">Byte offset inside the entry.</param>
    /// <param name="destination">The destination buffer.</param>
    /// <returns>The number of bytes read.</returns>
    /// <exception cref="InvalidDataException">Thrown when the entry does not exist.</exception>
    public int ReadEntryRange(string entryName, long entryOffset, Span<byte> destination)
    {
        if (!_reader.TryGetEntry(entryName, out PackageEntry? entry))
            throw new InvalidDataException($"Mesh asset entry '{entryName}' does not exist.");
        return _reader.ReadByEntry(entry, destination, entryOffset);
    }

    /// <summary>
    /// Reads a typed chunk into <paramref name="destination"/> and verifies its xxHash64 checksum.
    /// </summary>
    /// <param name="chunk">The chunk descriptor.</param>
    /// <param name="destination">The destination byte span matching the chunk size.</param>
    /// <exception cref="InvalidDataException">Thrown on missing entry, size mismatch, or hash mismatch.</exception>
    /// <exception cref="NotSupportedException">Thrown for codecs other than <see cref="MeshChunkCodec.None"/>.</exception>
    public void ReadChunk(MeshChunkMeta chunk, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        if (chunk.Codec != MeshChunkCodec.None)
            throw new NotSupportedException($"Mesh asset chunk '{chunk.Entry}' uses unsupported codec {chunk.Codec}.");
        if (!TryGetEntrySize(chunk.Entry, out long size))
            throw new InvalidDataException($"Mesh asset chunk '{chunk.Entry}' does not exist.");
        if (size != destination.Length)
            throw new InvalidDataException($"Mesh asset chunk '{chunk.Entry}' size mismatch: expected {destination.Length}, stored {size}.");

        ReadEntry(chunk.Entry, destination);
        ulong hash = XxHash64.HashToUInt64(destination);
        if (hash != chunk.Hash)
            throw new InvalidDataException($"Mesh asset chunk '{chunk.Entry}' hash mismatch: expected {chunk.Hash}, computed {hash}.");
    }

    /// <summary>
    /// Reads a typed chunk into a <see cref="SafeMemoryHandle"/> buffer and verifies its xxHash64 checksum.
    /// </summary>
    /// <param name="chunk">The chunk descriptor.</param>
    /// <param name="destination">The destination native buffer matching the chunk size.</param>
    public void ReadChunk(MeshChunkMeta chunk, SafeMemoryHandle destination) =>
        ReadChunk(chunk, destination.AsSpan());

    /// <summary>
    /// Reads and hash-verifies a tightly packed array of unmanaged structs from a chunk entry.
    /// </summary>
    /// <typeparam name="T">The unmanaged record type matching the on-disk layout.</typeparam>
    /// <param name="entry">The package entry identifying the chunk.</param>
    /// <returns>The decoded struct array.</returns>
    public T[] ReadStructChunk<T>(PackageEntry entry) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(entry);
        MeshChunkMeta chunk = GetChunk(entry.Name);
        if (entry.Size % sizeof(T) != 0)
            throw new InvalidDataException($"Mesh asset chunk '{entry.Name}' size {entry.Size} is not a multiple of {sizeof(T)} bytes.");
        T[] items = new T[checked((int)(entry.Size / sizeof(T)))];
        ReadChunk(chunk, MemoryMarshal.AsBytes(items.AsSpan()));
        return items;
    }

    /// <summary>Tries to find the typed chunk descriptor for an entry name.</summary>
    /// <param name="entryName">The entry name.</param>
    /// <param name="chunk">Receives the chunk descriptor when found.</param>
    /// <returns><see langword="true"/> when a descriptor exists for the entry.</returns>
    public bool TryGetChunk(string entryName, [NotNullWhen(true)] out MeshChunkMeta? chunk)
    {
        foreach (MeshChunkMeta candidate in Meta.Chunks)
        {
            if (candidate.Entry == entryName)
            {
                chunk = candidate;
                return true;
            }
        }
        chunk = null;
        return false;
    }

    /// <summary>
    /// Finds the typed chunk descriptor for an entry name.
    /// </summary>
    /// <param name="entryName">The entry name.</param>
    /// <returns>The chunk descriptor.</returns>
    /// <exception cref="InvalidDataException">Thrown when no descriptor references the entry.</exception>
    public MeshChunkMeta GetChunk(string entryName)
    {
        if (TryGetChunk(entryName, out MeshChunkMeta? chunk))
            return chunk;
        throw new InvalidDataException($"Mesh asset entry '{entryName}' has no chunk descriptor.");
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        _reader.Dispose();
        _stream.Dispose();
    }
}
