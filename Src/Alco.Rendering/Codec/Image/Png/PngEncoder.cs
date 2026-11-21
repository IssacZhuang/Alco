using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace Alco.Rendering;

/// <summary>Encodes non-interlaced RGBA8 PNG images.</summary>
internal static class PngEncoder
{
    private const int ScanlineBatchBytes = 64 * 1024;
    private const int ChunkHeaderBytes = 8;
    private const int ChunkOverhead = ChunkHeaderBytes + 4;
    private const int SignatureBytes = 8;
    private const int IhdrBytes = 13;
    private const int PngHeaderBytes = SignatureBytes + IhdrBytes + ChunkOverhead;

    private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Encodes a PNG to a caller-owned byte array.</summary>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height, PngEncodeMode mode, int rowStrideBytes)
        => EncodeCore(rgba, width, height, mode, rowStrideBytes, destination: null)!;

    /// <summary>Encodes PNG from a pointer to tightly packed RGBA8 pixels.</summary>
    public static unsafe byte[] Encode(byte* rgba, int width, int height)
    {
        int rowBytes = GetRowBytes(width, height);
        long length = (long)rowBytes * height;
        if (rgba == null || length > int.MaxValue)
        {
            throw new ImageEncodeException("A valid RGBA8 pixel pointer and a span-sized image are required.");
        }

        return Encode(new ReadOnlySpan<byte>(rgba, (int)length), width, height, PngEncodeMode.Balanced, rowBytes);
    }

    /// <summary>Writes one PNG to a writable stream without seeking or closing it.</summary>
    public static void Write(ReadOnlySpan<byte> rgba, int width, int height, Stream destination, PngEncodeMode mode, int rowStrideBytes)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException("The PNG destination must be writable.", nameof(destination));
        }

        EncodeCore(rgba, width, height, mode, rowStrideBytes, destination);
    }

    private static byte[]? EncodeCore(ReadOnlySpan<byte> rgba, int width, int height, PngEncodeMode mode, int rowStrideBytes, Stream? destination)
    {
        int rowBytes = GetRowBytes(width, height);
        if (rowStrideBytes == 0)
        {
            rowStrideBytes = rowBytes;
        }

        if (rowStrideBytes < rowBytes)
        {
            throw new ImageEncodeException($"RGBA8 row stride {rowStrideBytes} is smaller than {rowBytes} bytes.");
        }

        long requiredBytes = (height - 1L) * rowStrideBytes + rowBytes;
        if (rgba.Length < requiredBytes)
        {
            throw new ImageEncodeException($"Insufficient pixel data: expected {requiredBytes} bytes, got {rgba.Length}.");
        }

        ZLibCompressionOptions options = new()
        {
            CompressionLevel = mode switch
            {
                PngEncodeMode.Balanced => 5,
                PngEncodeMode.Fast => 1,
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            },
        };

        if (destination != null)
        {
            Span<byte> header = stackalloc byte[PngHeaderBytes];
            FormatHeader(header, width, height);
            destination.Write(header);
        }

        using IdatStream idat = new(destination);
        using (ZLibStream zlib = new(idat, options, leaveOpen: true))
        {
            WriteScanlines(rgba, height, rowBytes, rowStrideBytes, mode, zlib);
        }

        return idat.Finish(width, height);
    }

    private static int GetRowBytes(int width, int height)
    {
        if (width <= 0 || width > int.MaxValue / 4 || height <= 0)
        {
            throw new ImageEncodeException($"Invalid RGBA8 image dimensions: {width}x{height}.");
        }

        return width * 4;
    }

    private static void FormatHeader(Span<byte> header, int width, int height)
    {
        Signature.CopyTo(header);
        Span<byte> chunk = header[SignatureBytes..];
        Span<byte> ihdr = chunk.Slice(ChunkHeaderBytes, IhdrBytes);
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10..].Clear();
        FormatChunk(chunk, "IHDR"u8);
    }

    private static void FormatChunk(Span<byte> chunk, ReadOnlySpan<byte> type)
    {
        BinaryPrimitives.WriteInt32BigEndian(chunk, chunk.Length - ChunkOverhead);
        type.CopyTo(chunk[4..ChunkHeaderBytes]);
        BinaryPrimitives.WriteUInt32BigEndian(chunk[^4..], PngCrc32.Compute(chunk[4..^4]));
    }

    private static void WriteScanlines(ReadOnlySpan<byte> rgba, int height, int rowBytes, int rowStrideBytes, PngEncodeMode mode, ZLibStream zlib)
    {
        int scanlineBytes = rowBytes + 1;
        int rowsPerBatch = Math.Min(height, Math.Max(1, ScanlineBatchBytes / scanlineBytes));
        byte[] buffer = ArrayPool<byte>.Shared.Rent(rowsPerBatch * scanlineBytes);
        try
        {
            ReadOnlySpan<byte> previous = default;
            for (int y = 0; y < height;)
            {
                int count = Math.Min(rowsPerBatch, height - y);
                for (int i = 0; i < count; i++, y++)
                {
                    ReadOnlySpan<byte> row = rgba.Slice(y * rowStrideBytes, rowBytes);
                    PngFilter.FilterRow(row, previous, buffer.AsSpan(i * scanlineBytes, scanlineBytes), mode);
                    previous = row;
                }

                zlib.Write(buffer.AsSpan(0, count * scanlineBytes));
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private sealed class IdatStream : Stream
    {
        private const int ChunkBytes = 64 * 1024;
        private const int PayloadBytes = ChunkBytes - ChunkOverhead;

        private readonly Stream? _destination;
        private readonly List<byte[]>? _fullChunks;
        private byte[]? _buffer;
        private int _count;
        private bool _disposed;

        public IdatStream(Stream? destination)
        {
            _destination = destination;
            _fullChunks = destination == null ? new List<byte[]>() : null;
        }

        public override bool CanWrite => !_disposed;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> data)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            while (!data.IsEmpty)
            {
                byte[] buffer = _buffer ??= ArrayPool<byte>.Shared.Rent(ChunkBytes);
                int size = Math.Min(data.Length, PayloadBytes - _count);
                data[..size].CopyTo(buffer.AsSpan(ChunkHeaderBytes + _count, size));
                _count += size;
                data = data[size..];
                if (_count == PayloadBytes)
                {
                    PublishFullChunk(buffer);
                }
            }
        }

        public byte[]? Finish(int width, int height)
        {
            Span<byte> tail = default;
            if (_count != 0)
            {
                tail = _buffer.AsSpan(0, _count + ChunkOverhead);
                FormatChunk(tail, "IDAT"u8);
            }

            if (_destination != null)
            {
                if (!tail.IsEmpty)
                {
                    _destination.Write(tail);
                }

                Span<byte> end = stackalloc byte[ChunkOverhead];
                FormatChunk(end, "IEND"u8);
                _destination.Write(end);
                return null;
            }

            List<byte[]> fullChunks = _fullChunks!;
            int size = checked(PngHeaderBytes + fullChunks.Count * ChunkBytes + tail.Length + ChunkOverhead);
            byte[] png = GC.AllocateUninitializedArray<byte>(size);
            FormatHeader(png.AsSpan(0, PngHeaderBytes), width, height);
            int offset = PngHeaderBytes;
            for (int i = 0; i < fullChunks.Count; i++)
            {
                fullChunks[i].AsSpan(0, ChunkBytes).CopyTo(png.AsSpan(offset, ChunkBytes));
                offset += ChunkBytes;
            }

            tail.CopyTo(png.AsSpan(offset, tail.Length));
            FormatChunk(png.AsSpan(offset + tail.Length), "IEND"u8);
            return png;
        }

        private void PublishFullChunk(byte[] buffer)
        {
            Span<byte> chunk = buffer.AsSpan(0, ChunkBytes);
            FormatChunk(chunk, "IDAT"u8);
            _count = 0;
            if (_destination != null)
            {
                _destination.Write(chunk);
                return;
            }

            _fullChunks!.Add(buffer);
            _buffer = null;
        }

        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                if (_buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(_buffer);
                    _buffer = null;
                }

                if (_fullChunks != null)
                {
                    for (int i = 0; i < _fullChunks.Count; i++)
                    {
                        ArrayPool<byte>.Shared.Return(_fullChunks[i]);
                    }
                }
            }

            base.Dispose(disposing);
        }
    }
}
