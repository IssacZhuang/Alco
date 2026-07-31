using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace Alco.Rendering;

/// <summary>
/// Encodes RGBA8 pixel data to PNG format.
/// Implements PNG 1.2: IHDR, IDAT (zlib-compressed filtered scanlines), IEND.
/// Uses adaptive row filtering (minimum-sum heuristic) for optimal DEFLATE compression.
/// </summary>
internal static unsafe class PngEncoder
{
    /// <summary>PNG signature bytes.</summary>
    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>IHDR chunk type bytes.</summary>
    private static ReadOnlySpan<byte> IhdrType => "IHDR"u8;

    /// <summary>IDAT chunk type bytes.</summary>
    private static ReadOnlySpan<byte> IdatType => "IDAT"u8;

    /// <summary>IEND chunk type bytes.</summary>
    private static ReadOnlySpan<byte> IendType => "IEND"u8;

    /// <summary>
    /// Encode RGBA8 pixel data to a complete PNG file.
    /// </summary>
    /// <param name="rgba">RGBA8 pixel data (width * height * 4 bytes, row-major).</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>PNG-encoded file bytes.</returns>
    /// <exception cref="ImageEncodeException">Invalid dimensions or encoding failure.</exception>
    public static byte[] Encode(ReadOnlySpan<byte> rgba, int width, int height)
    {
        ValidateInput(rgba, width, height);

        int stride = width * 4;
        int filteredRowSize = 1 + stride; // filter byte + pixel bytes
        int filteredSize = height * filteredRowSize;

        // Allocate buffers
        byte[] filtered = ArrayPool<byte>.Shared.Rent(filteredSize);
        byte[] tempRow = ArrayPool<byte>.Shared.Rent(stride);

        try
        {
            // Step 1: Apply adaptive row filtering
            PngFilter.FilterAdaptive(rgba, width, height, filtered.AsSpan(0, filteredSize), tempRow.AsSpan(0, stride));

            // Step 2: Compress with zlib
            byte[] compressed = CompressZlib(filtered.AsSpan(0, filteredSize));

            // Step 3: Assemble PNG file
            return AssemblePng(width, height, compressed);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(filtered);
            ArrayPool<byte>.Shared.Return(tempRow);
        }
    }

    /// <summary>
    /// Encode RGBA8 pixel data from a pointer to a complete PNG file.
    /// </summary>
    public static byte[] Encode(byte* rgba, int width, int height)
    {
        int stride = width * 4;
        int totalBytes = height * stride;
        ReadOnlySpan<byte> span = new(rgba, totalBytes);
        return Encode(span, width, height);
    }

    private static void ValidateInput(ReadOnlySpan<byte> rgba, int width, int height)
    {
        if (width <= 0)
            throw new ImageEncodeException($"Width must be positive, got {width}.");

        if (height <= 0)
            throw new ImageEncodeException($"Height must be positive, got {height}.");

        long expectedBytes = (long)width * height * 4;
        if (rgba.Length < expectedBytes)
            throw new ImageEncodeException($"Insufficient pixel data: expected {expectedBytes} bytes for {width}x{height} RGBA8, got {rgba.Length}.");
    }

    /// <summary>
    /// Compress data using zlib (RFC 1950) wrapping DEFLATE (RFC 1951).
    /// Uses <see cref="ZLibStream"/> for standards-compliant zlib output.
    /// </summary>
    private static byte[] CompressZlib(ReadOnlySpan<byte> data)
    {
        using var outputStream = new MemoryStream(data.Length);

        // ZLibStream handles the 2-byte zlib header, DEFLATE compression, and Adler-32 trailer
        using (var zlibStream = new ZLibStream(outputStream, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlibStream.Write(data);
        }

        return outputStream.ToArray();
    }

    /// <summary>
    /// Assemble the complete PNG file from IHDR, compressed IDAT, and IEND chunks.
    /// </summary>
    private static byte[] AssemblePng(int width, int height, byte[] compressedData)
    {
        // Calculate total size: signature + IHDR + IDAT + IEND
        int pngSize =
            8 +                             // PNG signature
            (12 + 13) +                     // IHDR chunk: 4(len) + 4(type) + 13(data) + 4(crc)
            (12 + compressedData.Length) +  // IDAT chunk: 4(len) + 4(type) + N(data) + 4(crc)
            12;                             // IEND chunk: 4(len) + 4(type) + 0(data) + 4(crc)

        byte[] png = new byte[pngSize];
        int pos = 0;

        // PNG signature
        PngSignature.CopyTo(png.AsSpan(pos, 8));
        pos += 8;

        // IHDR chunk
        pos = WriteChunk(png, pos, IhdrType, WriteIHDR(width, height));

        // IDAT chunk
        pos = WriteChunk(png, pos, IdatType, compressedData);

        // IEND chunk
        WriteChunk(png, pos, IendType, ReadOnlySpan<byte>.Empty);

        return png;
    }

    /// <summary>
    /// Build the 13-byte IHDR data for an 8-bit RGBA image (color type 6, no interlace).
    /// </summary>
    private static byte[] WriteIHDR(int width, int height)
    {
        byte[] ihdr = new byte[13];
        WriteBigEndianInt32(ihdr, 0, width);
        WriteBigEndianInt32(ihdr, 4, height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // color type: RGBA
        ihdr[10] = 0;  // compression method: deflate
        ihdr[11] = 0;  // filter method: standard
        ihdr[12] = 0;  // interlace method: none
        return ihdr;
    }

    /// <summary>
    /// Write a PNG chunk: length (4 BE) + type (4) + data + CRC32 (4 BE).
    /// </summary>
    private static int WriteChunk(byte[] output, int offset, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        // Length (big-endian)
        WriteBigEndianInt32(output, offset, data.Length);
        offset += 4;

        // Chunk type
        type.CopyTo(output.AsSpan(offset, 4));
        offset += 4;

        // Chunk data
        data.CopyTo(output.AsSpan(offset, data.Length));
        offset += data.Length;

        // CRC32 over type + data
        uint crc = PngCrc32.Compute(type, data);
        WriteBigEndianUInt32(output, offset, crc);
        offset += 4;

        return offset;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteBigEndianInt32(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteBigEndianUInt32(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }
}
