using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Alco.IO;

/// <summary>
/// Reads and writes structured binary files with PackedBinary metadata and raw content.
/// </summary>
public static class StructuredFileUtility
{
    private const int MagicSize = 4;
    private const int HeaderSize = MagicSize + sizeof(int);

    /// <summary>
    /// Composes the full file bytes from metadata and content.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type implementing <see cref="IStructuredFileMeta"/>.</typeparam>
    /// <param name="meta">The metadata to encode as PackedBinary.</param>
    /// <param name="content">The raw content bytes.</param>
    /// <returns>The complete file as a byte array.</returns>
    public static byte[] Compose<TMeta>(TMeta meta, ReadOnlySpan<byte> content)
        where TMeta : IStructuredFileMeta, new()
    {
        byte[] metaBytes = PackedBinarySerializer.Encode(meta, elideDefaults: false);
        int contentLengthOffset = checked(HeaderSize + metaBytes.Length);
        int contentOffset = checked(contentLengthOffset + sizeof(int));
        byte[] data = new byte[checked(contentOffset + content.Length)];

        TMeta.Magic.CopyTo(data.AsSpan(0, MagicSize));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(MagicSize), metaBytes.Length);
        metaBytes.AsSpan().CopyTo(data.AsSpan(HeaderSize));
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(contentLengthOffset), content.Length);
        content.CopyTo(data.AsSpan(contentOffset));

        return data;
    }

    /// <summary>
    /// Writes the structured file to a stream.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type implementing <see cref="IStructuredFileMeta"/>.</typeparam>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="meta">The metadata to encode as PackedBinary.</param>
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
    /// <param name="data">File data; keep it unchanged while using decoded metadata views or content.</param>
    /// <returns>The deserialized metadata and a slice of the content bytes.</returns>
    public static (TMeta Meta, ReadOnlyMemory<byte> Content) Read<TMeta>(ReadOnlyMemory<byte> data)
        where TMeta : IStructuredFileMeta, new()
    {
        ReadOnlyMemory<byte> metaBytes = SliceMeta<TMeta>(data);
        int contentLengthOffset = HeaderSize + metaBytes.Length;
        if (data.Length - contentLengthOffset < sizeof(int))
        {
            throw new InvalidDataException("Invalid file length.");
        }

        int contentOffset = contentLengthOffset + sizeof(int);
        int contentLength = BinaryPrimitives.ReadInt32LittleEndian(data.Span[contentLengthOffset..]);
        if (contentLength < 0 || contentLength > data.Length - contentOffset)
        {
            throw new InvalidDataException("Invalid content length.");
        }

        return (PackedBinarySerializer.Decode<TMeta>(metaBytes), data.Slice(contentOffset, contentLength));
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
    /// <param name="data">File header and metadata; keep them unchanged while using decoded metadata views.</param>
    /// <returns>The deserialized metadata.</returns>
    public static TMeta ReadMeta<TMeta>(ReadOnlyMemory<byte> data)
        where TMeta : IStructuredFileMeta, new()
    {
        return PackedBinarySerializer.Decode<TMeta>(SliceMeta<TMeta>(data));
    }

    /// <summary>
    /// Reads the header and metadata from the current position without consuming the content length or content.
    /// </summary>
    /// <typeparam name="TMeta">The metadata type implementing <see cref="IStructuredFileMeta"/>.</typeparam>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>The deserialized metadata.</returns>
    public static TMeta ReadMeta<TMeta>(Stream stream)
        where TMeta : IStructuredFileMeta, new()
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        ReadRequired(stream, header);
        int metaLength = ReadMetaLength(header, TMeta.Magic);
        return PackedBinarySerializer.Decode<TMeta>(ReadMetaBytes(stream, metaLength));
    }

    private static ReadOnlyMemory<byte> SliceMeta<TMeta>(ReadOnlyMemory<byte> data)
        where TMeta : IStructuredFileMeta
    {
        int metaLength = ReadMetaLength(data.Span, TMeta.Magic);
        if (metaLength > data.Length - HeaderSize)
        {
            throw new InvalidDataException("Invalid meta length.");
        }

        return data.Slice(HeaderSize, metaLength);
    }

    private static int ReadMetaLength(ReadOnlySpan<byte> header, ReadOnlySpan<byte> magic)
    {
        if (header.Length < HeaderSize || !header[..MagicSize].SequenceEqual(magic))
        {
            throw new InvalidDataException($"Invalid magic. Expected '{Encoding.ASCII.GetString(magic)}'.");
        }

        int metaLength = BinaryPrimitives.ReadInt32LittleEndian(header[MagicSize..]);
        if (metaLength < 0)
        {
            throw new InvalidDataException("Invalid meta length.");
        }

        return metaLength;
    }

    private static ReadOnlyMemory<byte> ReadMetaBytes(Stream stream, int metaLength)
    {
        if (metaLength > Array.MaxLength || (stream.CanSeek && metaLength > stream.Length - stream.Position))
        {
            throw new InvalidDataException("Invalid meta length.");
        }

        const int chunkSize = 4096;
        if (stream.CanSeek || metaLength <= chunkSize)
        {
            byte[] bytes = new byte[metaLength];
            ReadRequired(stream, bytes);
            return bytes;
        }

        // For forward-only streams, allocate in proportion to bytes actually read.
        var buffer = new ArrayBufferWriter<byte>(chunkSize);
        while (buffer.WrittenCount < metaLength)
        {
            int count = Math.Min(chunkSize, metaLength - buffer.WrittenCount);
            ReadRequired(stream, buffer.GetSpan(count)[..count]);
            buffer.Advance(count);
        }

        return buffer.WrittenMemory;
    }

    private static void ReadRequired(Stream stream, Span<byte> buffer)
    {
        if (stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) != buffer.Length)
        {
            throw new InvalidDataException("Invalid stream length.");
        }
    }
}
