using System.Buffers.Binary;
using System.IO;
using System.Text;
using Alco;

namespace Alco.IO;

/// <summary>
/// Represents the result of reading a structured file, containing metadata and raw content bytes.
/// </summary>
public readonly struct StructuredFileData<TMeta> where TMeta : ISerializable, new()
{
    /// <summary>
    /// Gets the metadata deserialized from the meta section.
    /// </summary>
    public TMeta Meta { get; }

    /// <summary>
    /// Gets the raw content bytes. Must remain alive while this memory is used.
    /// </summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="StructuredFileData{TMeta}"/> struct.
    /// </summary>
    public StructuredFileData(TMeta meta, ReadOnlyMemory<byte> content)
    {
        Meta = meta;
        Content = content;
    }
}

/// <summary>
/// Abstract base class for structured binary files with the format: [magic][metaLength][meta][contentLength][content].
/// </summary>
/// <typeparam name="TMeta">The metadata type, must implement <see cref="ISerializable"/>.</typeparam>
public abstract class StructuredFile<TMeta> where TMeta : ISerializable, new()
{
    /// <summary>
    /// Gets the magic number that identifies this file format. Must return a span backed by a stable byte array.
    /// </summary>
    protected abstract ReadOnlySpan<byte> Magic { get; }

    /// <summary>
    /// Composes the full file bytes from metadata and content.
    /// </summary>
    /// <param name="meta">The metadata to encode via BinaryParser.</param>
    /// <param name="content">The raw content bytes.</param>
    /// <returns>The complete file as a byte array.</returns>
    public byte[] Compose(TMeta meta, ReadOnlySpan<byte> content)
    {
        ReadOnlyMemory<byte> metaBytes = BinaryParser.Encode(meta);
        int metaLength = metaBytes.Length;
        int contentLength = content.Length;

        byte[] data = new byte[4 + 4 + metaLength + 4 + contentLength];
        Magic.CopyTo(data.AsSpan(0, 4));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4, 4), metaLength);
        metaBytes.Span.CopyTo(data.AsSpan(8, metaLength));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8 + metaLength, 4), contentLength);
        content.CopyTo(data.AsSpan(12 + metaLength, contentLength));

        return data;
    }

    /// <summary>
    /// Writes the structured file to a stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="meta">The metadata to encode via BinaryParser.</param>
    /// <param name="content">The raw content bytes.</param>
    public void WriteTo(Stream stream, TMeta meta, ReadOnlySpan<byte> content)
    {
        byte[] data = Compose(meta, content);
        stream.Write(data);
    }

    /// <summary>
    /// Reads both metadata and content from memory data.
    /// </summary>
    /// <param name="data">The full file data. Must remain alive while the returned content memory is used.</param>
    /// <returns>A <see cref="StructuredFileData{TMeta}"/> containing the metadata and content.</returns>
    public StructuredFileData<TMeta> Read(ReadOnlyMemory<byte> data)
    {
        return Parse(data, Magic);
    }

    /// <summary>
    /// Reads both metadata and content from a stream by buffering into memory.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>A <see cref="StructuredFileData{TMeta}"/> containing the metadata and content.</returns>
    public StructuredFileData<TMeta> Read(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Read(ms.ToArray());
    }

    /// <summary>
    /// Reads only the metadata from memory data.
    /// </summary>
    /// <param name="data">The full file data.</param>
    /// <returns>The deserialized metadata.</returns>
    public TMeta ReadMeta(ReadOnlySpan<byte> data)
    {
        return ParseMeta(data, Magic);
    }

    /// <summary>
    /// Reads only the metadata from a stream. Reads exactly 8 + metaLength bytes from the current position.
    /// The contentLength field (next 4 bytes) is NOT consumed.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>The deserialized metadata.</returns>
    public TMeta ReadMeta(Stream stream)
    {
        return ParseMeta(stream, Magic);
    }

    /// <summary>
    /// Parses both metadata and content from memory data using the specified magic number.
    /// </summary>
    /// <param name="data">The full file data.</param>
    /// <param name="magic">The expected magic number.</param>
    /// <returns>A <see cref="StructuredFileData{TMeta}"/> containing the metadata and content.</returns>
    public static StructuredFileData<TMeta> Parse(ReadOnlyMemory<byte> data, ReadOnlySpan<byte> magic)
    {
        ReadOnlySpan<byte> span = data.Span;
        ValidateMagicAndReadMeta(span, magic, out int metaLength, out TMeta meta);

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

        return new StructuredFileData<TMeta>(meta, data.Slice(12 + metaLength, contentLength));
    }

    /// <summary>
    /// Parses only the metadata from memory data using the specified magic number.
    /// </summary>
    /// <param name="data">The full file data.</param>
    /// <param name="magic">The expected magic number.</param>
    /// <returns>The deserialized metadata.</returns>
    public static TMeta ParseMeta(ReadOnlySpan<byte> data, ReadOnlySpan<byte> magic)
    {
        ValidateMagicAndReadMeta(data, magic, out _, out TMeta meta);
        return meta;
    }

    /// <summary>
    /// Parses only the metadata from a stream using the specified magic number.
    /// Reads exactly 8 + metaLength bytes. Does NOT consume the contentLength field.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <param name="magic">The expected magic number.</param>
    /// <returns>The deserialized metadata.</returns>
    public static TMeta ParseMeta(Stream stream, ReadOnlySpan<byte> magic)
    {
        Span<byte> header = stackalloc byte[8];
        int bytesRead = stream.Read(header);
        if (bytesRead < 8 || !header.Slice(0, 4).SequenceEqual(magic))
        {
            throw new InvalidDataException($"Invalid magic. Expected '{Encoding.ASCII.GetString(magic)}'.");
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

    private static void ValidateMagicAndReadMeta(ReadOnlySpan<byte> data, ReadOnlySpan<byte> magic, out int metaLength, out TMeta meta)
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

        meta = BinaryParser.Decode<TMeta>(data.Slice(8, metaLength));
    }
}
