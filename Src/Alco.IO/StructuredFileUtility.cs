using System.Buffers.Binary;
using System.IO;
using System.Text;
using Alco;

namespace Alco.IO;

/// <summary>
/// Provides static utility methods for reading and writing structured binary files
/// with the format: [magic][metaLength][meta][contentLength][content].
/// </summary>
public static class StructuredFileUtility
{
    /// <summary>
    /// Composes the full file bytes from metadata and content.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type implementing <see cref="IStructuredFileMeta"/>.</typeparam>
    /// <param name="meta">The metadata to encode via BinaryParser.</param>
    /// <param name="content">The raw content bytes.</param>
    /// <returns>The complete file as a byte array.</returns>
    public static byte[] Compose<TMeta>(TMeta meta, ReadOnlySpan<byte> content)
        where TMeta : IStructuredFileMeta, new()
    {
        ReadOnlyMemory<byte> metaBytes = BinaryParser.Encode(meta);
        int metaLength = metaBytes.Length;
        int contentLength = content.Length;

        byte[] data = new byte[4 + 4 + metaLength + 4 + contentLength];
        TMeta.Magic.CopyTo(data.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4, 4), metaLength);
        metaBytes.Span.CopyTo(data.AsSpan(8, metaLength));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + metaLength, 4), contentLength);
        content.CopyTo(data.AsSpan(12 + metaLength, contentLength));

        return data;
    }

    /// <summary>
    /// Writes the structured file to a stream.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type implementing <see cref="IStructuredFileMeta"/>.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="meta">The metadata to encode via BinaryParser.</param>
    /// <param name="content">The raw content bytes.</param>
    public static void WriteTo<TMeta>(Stream stream, TMeta meta, ReadOnlySpan<byte> content)
        where TMeta : IStructuredFileMeta, new()
    {
        byte[] data = Compose<TMeta>(meta, content);
        stream.Write(data);
    }

    /// <summary>
    /// Reads both metadata and content from memory data.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type implementing <see cref="IStructuredFileMeta"/>.</typeparam>
    /// <param name="data">The full file data. Must remain alive while the returned content memory is used.</param>
    /// <returns>A tuple containing the deserialized metadata and the content bytes.</returns>
    public static (TMeta Meta, ReadOnlyMemory<byte> Content) Read<TMeta>(ReadOnlyMemory<byte> data)
        where TMeta : IStructuredFileMeta, new()
    {
        ReadOnlySpan<byte> span = data.Span;
        TMeta meta = ValidateMagicAndReadMeta<TMeta>(span, TMeta.Magic, out int metaLength);

        if (span.Length < 12 + metaLength)
        {
            throw new InvalidDataException("Invalid file length.");
        }

        int contentLength = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(8 + metaLength, 4));

        if (contentLength < 0)
        {
            throw new InvalidDataException("Invalid content length.");
        }

        if (span.Length < 12 + metaLength + contentLength)
        {
            throw new InvalidDataException("Invalid file length.");
        }

        return (meta, data.Slice(12 + metaLength, contentLength));
    }

    /// <summary>
    /// Reads both metadata and content from a stream by buffering into memory.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type implementing <see cref="IStructuredFileMeta"/>.</typeparam>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>A tuple containing the deserialized metadata and the content bytes.</returns>
    public static (TMeta Meta, ReadOnlyMemory<byte> Content) Read<TMeta>(Stream stream)
        where TMeta : IStructuredFileMeta, new()
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Read<TMeta>(ms.ToArray());
    }

    /// <summary>
    /// Reads only the metadata from memory data.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type implementing <see cref="IStructuredFileMeta"/>.</typeparam>
    /// <param name="data">The full file data.</param>
    /// <returns>The deserialized metadata.</returns>
    public static TMeta ReadMeta<TMeta>(ReadOnlyMemory<byte> data)
        where TMeta : IStructuredFileMeta, new()
    {
        return ValidateMagicAndReadMeta<TMeta>(data.Span, TMeta.Magic, out _);
    }

    /// <summary>
    /// Reads only the metadata from a stream. Reads exactly 8 + metaLength bytes from the current position.
    /// The contentLength field (next 4 bytes) is NOT consumed.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type implementing <see cref="IStructuredFileMeta"/>.</typeparam>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>The deserialized metadata.</returns>
    public static TMeta ReadMeta<TMeta>(Stream stream)
        where TMeta : IStructuredFileMeta, new()
    {
        Span<byte> header = stackalloc byte[8];
        int bytesRead = stream.Read(header);
        if (bytesRead < 8 || !header.Slice(0, 4).SequenceEqual(TMeta.Magic))
        {
            throw new InvalidDataException($"Invalid magic. Expected '{Encoding.ASCII.GetString(TMeta.Magic)}'.");
        }

        int metaLength = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4, 4));

        if (metaLength < 0)
        {
            throw new InvalidDataException("Invalid meta length.");
        }

        byte[] metaData = new byte[metaLength];
        bytesRead = stream.Read(metaData, 0, metaLength);
        if (bytesRead < metaLength)
        {
            throw new InvalidDataException("Invalid stream length.");
        }

        return BinaryParser.Decode<TMeta>(metaData);
    }

    /// <summary>
    /// Validates the magic number and reads the metadata from a byte span.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type implementing <see cref="IStructuredFileMeta"/>.</typeparam>
    /// <param name="data">The file data to read from.</param>
    /// <param name="magic">The expected magic number.</param>
    /// <param name="metaLength">The length of the meta section in bytes.</param>
    /// <returns>The deserialized metadata.</returns>
    /// <exception cref="InvalidDataException">Thrown when the magic number, meta length, or file length is invalid.</exception>
    private static TMeta ValidateMagicAndReadMeta<TMeta>(ReadOnlySpan<byte> data, ReadOnlySpan<byte> magic, out int metaLength)
        where TMeta : IStructuredFileMeta, new()
    {
        if (data.Length < 8 || !data.Slice(0, 4).SequenceEqual(magic))
        {
            throw new InvalidDataException($"Invalid magic. Expected '{Encoding.ASCII.GetString(magic)}'.");
        }

        metaLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(4, 4));

        if (metaLength < 0)
        {
            throw new InvalidDataException("Invalid meta length.");
        }

        if (data.Length < 8 + metaLength)
        {
            throw new InvalidDataException("Invalid file length.");
        }

        return BinaryParser.Decode<TMeta>(data.Slice(8, metaLength));
    }
}
