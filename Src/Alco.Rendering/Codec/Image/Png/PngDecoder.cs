using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;

namespace Alco.Rendering;

/// <summary>
/// Decodes PNG image data to RGBA8 pixel buffer.
/// Orchestrates chunk parsing, zlib decompression, row defiltering, Adam7 deinterlacing,
/// and color type conversion.
/// </summary>
internal static unsafe class PngDecoder
{
    private const int HeaderSize = 33;
    private const int MaxPooledIdatBytes = 1 << 20;
    private const uint Ihdr = 0x49484452;
    private const uint Plte = 0x504C5445;
    private const uint Trns = 0x74524E53;
    private const uint Idat = 0x49444154;
    private const uint Iend = 0x49454E44;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Decode PNG data to RGBA8. Caller owns the returned pointer and must call <c>NativeMemory.Free</c>.
    /// </summary>
    /// <param name="data">Complete PNG file bytes.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    /// <returns>Pointer to RGBA8 pixel data. Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="ImageDecodeException">Invalid or unsupported PNG data.</exception>
    public static byte* Decode(ReadOnlySpan<byte> data, out int width, out int height)
    {
        ReadHeader(data, out width, out height, out int bitDepth, out int colorType, out int interlace);

        ReadOnlySpan<byte> palette = default;
        ReadOnlySpan<byte> transparency = default;
        int idatStart = 0;
        int idatEnd = 0;
        int idatLength = 0;
        int idatChunkCount = 0;
        bool transparencySeen = false;
        bool endSeen = false;
        int pos = HeaderSize;

        while (pos < data.Length && !endSeen)
        {
            int chunkStart = pos;
            ReadOnlySpan<byte> chunkData = ReadChunk(data, ref pos, out uint chunkType);

            switch (chunkType)
            {
                case Ihdr:
                    throw new ImageDecodeException("Duplicate IHDR chunk.");

                case Plte:
                    if (idatChunkCount != 0 || !palette.IsEmpty || transparencySeen ||
                        colorType is 0 or 4 || chunkData.Length is < 3 or > 768 || chunkData.Length % 3 != 0 ||
                        (colorType == 3 && chunkData.Length / 3 > (1 << bitDepth)))
                        throw new ImageDecodeException("Invalid PLTE chunk.");

                    palette = chunkData;
                    break;

                case Trns:
                    if (idatChunkCount != 0 || transparencySeen ||
                        !IsValidTransparency(chunkData.Length, colorType, palette.Length / 3))
                        throw new ImageDecodeException("Invalid tRNS chunk.");

                    transparency = chunkData;
                    transparencySeen = true;
                    break;

                case Idat:
                    if (idatChunkCount == 0)
                        idatStart = chunkStart;
                    else if (chunkStart != idatEnd)
                        throw new ImageDecodeException("IDAT chunks must be consecutive.");

                    idatLength += chunkData.Length;
                    idatEnd = pos;
                    idatChunkCount++;
                    break;

                case Iend:
                    if (!chunkData.IsEmpty)
                        throw new ImageDecodeException("Invalid IEND chunk.");

                    endSeen = true;
                    break;

                default:
                    if ((chunkType & 0x20000000u) == 0)
                        throw new ImageDecodeException($"Unsupported critical PNG chunk: 0x{chunkType:X8}.");
                    break;
            }
        }

        if (!endSeen)
            throw new ImageDecodeException("No IEND chunk found.");

        if (idatLength < 6)
            throw new ImageDecodeException("Missing or incomplete PNG image data.");

        if (colorType == 3 && palette.IsEmpty)
            throw new ImageDecodeException("Indexed color type (3) requires PLTE chunk.");

        if (idatChunkCount == 1)
            return DecodePixels(data.Slice(idatStart + 8, idatLength), width, height, bitDepth, colorType, interlace, palette, transparency);

        bool pooled = idatLength <= MaxPooledIdatBytes;
        byte[] combined = pooled ? ArrayPool<byte>.Shared.Rent(idatLength) : GC.AllocateUninitializedArray<byte>(idatLength);
        try
        {
            Span<byte> idatData = combined.AsSpan(0, idatLength);
            CopyIdatData(data[idatStart..idatEnd], idatData);
            return DecodePixels(idatData, width, height, bitDepth, colorType, interlace, palette, transparency);
        }
        finally
        {
            if (pooled)
                ArrayPool<byte>.Shared.Return(combined);
        }
    }

    /// <summary>
    /// Read PNG header dimensions without full decode.
    /// </summary>
    /// <param name="data">Complete PNG file bytes.</param>
    /// <returns>Image width and height in pixels.</returns>
    /// <exception cref="ImageDecodeException">Invalid PNG header.</exception>
    public static (int Width, int Height) GetInfo(ReadOnlySpan<byte> data)
    {
        ReadHeader(data, out int width, out int height, out _, out _, out _);
        return (width, height);
    }

    #region Chunk Parsing Helpers

    private static void ReadHeader(ReadOnlySpan<byte> data, out int width, out int height,
        out int bitDepth, out int colorType, out int interlace)
    {
        if (data.Length < HeaderSize || !data[..8].SequenceEqual(PngSignature))
            throw new ImageDecodeException("Invalid or truncated PNG header.");

        if (BinaryPrimitives.ReadUInt32BigEndian(data[8..]) != 13 || BinaryPrimitives.ReadUInt32BigEndian(data[12..]) != Ihdr)
            throw new ImageDecodeException("PNG must start with a 13-byte IHDR chunk.");

        ValidateCrc(data[12..29], data[29..]);
        ReadOnlySpan<byte> ihdr = data[16..29];
        width = BinaryPrimitives.ReadInt32BigEndian(ihdr);
        height = BinaryPrimitives.ReadInt32BigEndian(ihdr[4..]);
        bitDepth = ihdr[8];
        colorType = ihdr[9];
        int compression = ihdr[10];
        int filter = ihdr[11];
        interlace = ihdr[12];

        if (width <= 0 || height <= 0 || (long)width * height > int.MaxValue / 4)
            throw new ImageDecodeException($"Invalid PNG dimensions: {width}x{height}.");

        if (bitDepth is not (1 or 2 or 4 or 8 or 16))
            throw new ImageDecodeException($"Unsupported bit depth: {bitDepth}.");

        if (colorType is not (0 or 2 or 3 or 4 or 6))
            throw new ImageDecodeException($"Unsupported color type: {colorType}.");

        if ((colorType == 3 && bitDepth == 16) || (colorType is 2 or 4 or 6 && bitDepth < 8))
            throw new ImageDecodeException($"Invalid bit depth {bitDepth} for color type {colorType}.");

        if (compression != 0)
            throw new ImageDecodeException($"Unsupported compression method: {compression}.");

        if (filter != 0)
            throw new ImageDecodeException($"Unsupported filter method: {filter}.");

        if (interlace is not (0 or 1))
            throw new ImageDecodeException($"Unsupported interlace method: {interlace}.");
    }

    private static ReadOnlySpan<byte> ReadChunk(ReadOnlySpan<byte> data, scoped ref int pos, out uint type)
    {
        int remaining = data.Length - pos;
        if (remaining < 12)
            throw new ImageDecodeException("Truncated PNG chunk.");

        uint length = BinaryPrimitives.ReadUInt32BigEndian(data[pos..]);
        if (length > (uint)(remaining - 12))
            throw new ImageDecodeException("PNG chunk exceeds the input size.");

        type = BinaryPrimitives.ReadUInt32BigEndian(data[(pos + 4)..]);
        int dataStart = pos + 8;
        int crcStart = dataStart + (int)length;
        ValidateCrc(data[(pos + 4)..crcStart], data[crcStart..]);
        pos = crcStart + 4;
        return data[dataStart..crcStart];
    }

    private static void ValidateCrc(ReadOnlySpan<byte> typeAndData, ReadOnlySpan<byte> crc)
    {
        if (PngCrc32.Compute(typeAndData) != BinaryPrimitives.ReadUInt32BigEndian(crc))
            throw new ImageDecodeException("PNG chunk CRC mismatch.");
    }

    private static bool IsValidTransparency(int length, int colorType, int paletteSize) => colorType switch
    {
        0 => length == 2,
        2 => length == 6,
        3 => paletteSize > 0 && length <= paletteSize,
        _ => false,
    };

    private static void CopyIdatData(ReadOnlySpan<byte> chunks, Span<byte> destination)
    {
        while (!chunks.IsEmpty)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(chunks);
            chunks.Slice(8, length).CopyTo(destination);
            destination = destination[length..];
            chunks = chunks[(length + 12)..];
        }
    }

    #endregion

    #region Format Calculations

    /// <summary>
    /// Get the number of bytes per complete pixel sample for defiltering purposes.
    /// For sub-byte depths, this returns 1 (defiltering operates on byte boundaries).
    /// </summary>
    private static int GetBytesPerPixel(int bitDepth, int colorType)
    {
        return colorType switch
        {
            0 => Math.Max(1, bitDepth / 8), // grayscale
            2 => 3 * (bitDepth / 8),  // RGB
            3 => 1,                    // indexed (always 1 for defiltering)
            4 => 2 * (bitDepth / 8),  // grayscale + alpha
            6 => 4 * (bitDepth / 8),  // RGBA
            _ => throw new ImageDecodeException($"Unsupported color type: {colorType}.")
        };
    }

    /// <summary>
    /// Get the stride (bytes per row of pixel data, excluding filter byte) for the source format.
    /// </summary>
    private static int GetStride(int width, int bitDepth, int colorType)
    {
        int bitsPerPixel = colorType switch
        {
            0 => bitDepth,       // grayscale
            2 => bitDepth * 3,   // RGB
            3 => bitDepth,       // indexed
            4 => bitDepth * 2,   // grayscale + alpha
            6 => bitDepth * 4,   // RGBA
            _ => throw new ImageDecodeException($"Unsupported color type: {colorType}.")
        };

        return GetBufferSize(((long)width * bitsPerPixel + 7) / 8);
    }

    private static int GetBufferSize(long bytes)
    {
        if (bytes > int.MaxValue)
            throw new ImageDecodeException("PNG scanlines exceed the supported buffer size.");

        return (int)bytes;
    }

    #endregion

    private static byte* DecodePixels(ReadOnlySpan<byte> idatData, int width, int height,
        int bitDepth, int colorType, int interlace, ReadOnlySpan<byte> palette, ReadOnlySpan<byte> transparency)
    {
        const int presetDictionaryFlag = 0x20;
        if ((idatData[1] & presetDictionaryFlag) != 0)
            throw new ImageDecodeException("PNG zlib streams cannot use a preset dictionary.");

        int stride = GetStride(width, bitDepth, colorType);
        int bytesPerPixel = GetBytesPerPixel(bitDepth, colorType);
        if (interlace == 0)
            return DecodeNonInterlaced(idatData, width, height, bitDepth, colorType, bytesPerPixel, stride, palette, transparency);

        return DecodeInterlaced(idatData, width, height, bitDepth, colorType, bytesPerPixel, stride, palette, transparency);
    }

    private static byte* DecodeNonInterlaced(ReadOnlySpan<byte> idatData, int width, int height,
        int bitDepth, int colorType, int bytesPerPixel, int stride, ReadOnlySpan<byte> palette, ReadOnlySpan<byte> transparency)
    {
        int size = GetBufferSize(height * (1L + stride));
        byte* scanlines = (byte*)NativeMemory.Alloc((nuint)size);
        try
        {
            Span<byte> filtered = new(scanlines, size);
            InflateExact(idatData, filtered);
            if (colorType == 6 && bitDepth == 8)
            {
                PngDefilter.CompactRgba8(filtered, height, stride);
                byte* pixels = scanlines;
                scanlines = null;
                return pixels;
            }

            PngDefilter.Defilter(filtered, width, height, bytesPerPixel, stride);
            return ConvertToPixels(scanlines, width, height, stride, bitDepth, colorType, palette, transparency, hasFilterBytes: true);
        }
        finally
        {
            NativeMemory.Free(scanlines);
        }
    }

    private static void InflateExact(ReadOnlySpan<byte> idatData, Span<byte> filtered)
    {
        fixed (byte* compressed = idatData)
        {
            using IdatInputStream input = new(compressed, idatData.Length);
            using ZLibStream zlib = new(input, CompressionMode.Decompress, leaveOpen: true);
            try
            {
                zlib.ReadExactly(filtered);
                // A full output buffer does not guarantee that zlib has consumed and checked its trailer.
                Span<byte> extra = stackalloc byte[1];
                if (zlib.Read(extra) != 0)
                    throw new ImageDecodeException("PNG decompressed data exceeds the expected scanline size.");
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                throw new ImageDecodeException("Invalid or incomplete PNG zlib stream.", ex);
            }
        }
    }

    private sealed class IdatInputStream(byte* data, int length) : UnmanagedMemoryStream(data, length)
    {
        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);
            // ZLibStream can otherwise report EOF for a truncated stream with a missing trailer.
            if (read == 0 && count != 0)
                throw new EndOfStreamException("Unexpected end of PNG zlib input.");

            return read;
        }
    }

    #region Interlaced (Adam7) Decode

    private static byte* DecodeInterlaced(ReadOnlySpan<byte> idatData, int width, int height,
        int bitDepth, int colorType, int bytesPerPixel, int stride, ReadOnlySpan<byte> palette, ReadOnlySpan<byte> transparency)
    {
        Span<(int Width, int Height, int Stride, int Size)> passes = stackalloc (int, int, int, int)[7];
        long totalSize = 0;
        for (int pass = 0; pass < passes.Length; pass++)
        {
            var (pw, ph) = PngAdam7.GetPassSize(pass, width, height);
            int passStride = GetStride(pw, bitDepth, colorType);
            int passSize = ph == 0 ? 0 : GetBufferSize(ph * (1L + passStride));
            passes[pass] = (pw, ph, passStride, passSize);
            totalSize += passSize;
        }

        int size = GetBufferSize(totalSize);
        bool packedSamples = bitDepth < 8;
        int mergedSize = packedSamples ? width * height * 4 : GetBufferSize((long)stride * height);
        byte* decompressed = (byte*)NativeMemory.Alloc((nuint)size);
        try
        {
            Span<byte> scanlines = new(decompressed, size);
            InflateExact(idatData, scanlines);

            byte* merged = (byte*)NativeMemory.Alloc((nuint)mergedSize);
            try
            {
                for (int pass = 0; pass < passes.Length; pass++)
                {
                    var (pw, ph, passStride, passSize) = passes[pass];
                    if (passSize == 0)
                        continue;

                    Span<byte> passData = scanlines[..passSize];
                    scanlines = scanlines[passSize..];
                    PngDefilter.Defilter(passData, pw, ph, bytesPerPixel, passStride);
                    if (packedSamples)
                        ConvertInterlacedPassToRGBA8(merged, passData, passStride, pass, width, pw, ph, bitDepth, colorType, palette, transparency);
                    else
                        PngAdam7.MergePass(merged, stride, passData, 1 + passStride, pass, width, height, bytesPerPixel);
                }

                if (!packedSamples)
                    return ConvertToPixels(merged, width, height, stride, bitDepth, colorType, palette, transparency, hasFilterBytes: false);

                byte* pixels = merged;
                merged = null;
                return pixels;
            }
            finally
            {
                NativeMemory.Free(merged);
            }
        }
        finally
        {
            NativeMemory.Free(decompressed);
        }
    }

    /// <summary>
    /// Convert a single Adam7 pass of sub-byte pixels directly to RGBA8 output.
    /// Each pixel is placed at its correct (destX, destY) position in the output buffer.
    /// </summary>
    private static unsafe void ConvertInterlacedPassToRGBA8(
        byte* output, ReadOnlySpan<byte> passData, int passStride,
        int pass, int imageWidth,
        int passWidth, int passHeight, int bitDepth, int colorType,
        ReadOnlySpan<byte> palette, ReadOnlySpan<byte> transparency)
    {
        var (startX, startY, incX, incY) = PngAdam7.PassParams[pass];
        int mask = (1 << bitDepth) - 1;
        int transparentGray = colorType == 0 && !transparency.IsEmpty ? BinaryPrimitives.ReadUInt16BigEndian(transparency) : -1;

        for (int y = 0; y < passHeight; y++)
        {
            int passRowOffset = y * (1 + passStride) + 1; // Skip filter byte
            int destY = startY + y * incY;
            byte* dstRow = output + destY * imageWidth * 4;

            int bitPos = 0;
            int byteIdx = 0;

            for (int x = 0; x < passWidth; x++)
            {
                int destX = startX + x * incX;
                int shift = 8 - bitDepth - bitPos;
                int sample = (passData[passRowOffset + byteIdx] >> shift) & mask;

                // Convert sample to RGBA8 based on color type
                byte r, g, b, a;
                switch (colorType)
                {
                    case 0: // Grayscale
                        {
                            byte gray = ScaleToByte(sample, bitDepth);
                            r = g = b = gray;
                            a = sample == transparentGray ? (byte)0 : (byte)0xFF;
                        }
                        break;
                    case 3: // Indexed
                        {
                            int index = sample;
                            int maxIndex = palette.Length / 3 - 1;
                            if (index > maxIndex)
                                index = maxIndex;
                            r = palette[index * 3 + 0];
                            g = palette[index * 3 + 1];
                            b = palette[index * 3 + 2];
                            a = (index < transparency.Length)
                                ? transparency[index]
                                : (byte)0xFF;
                        }
                        break;
                    default:
                        r = g = b = a = 0;
                        break;
                }

                dstRow[destX * 4 + 0] = r;
                dstRow[destX * 4 + 1] = g;
                dstRow[destX * 4 + 2] = b;
                dstRow[destX * 4 + 3] = a;

                bitPos += bitDepth;
                if (bitPos == 8)
                {
                    bitPos = 0;
                    byteIdx++;
                }
            }
        }
    }

    #endregion

    #region Color Type Conversion

    private static byte* ConvertToPixels(byte* source, int width, int height, int sourceStride,
        int bitDepth, int colorType, ReadOnlySpan<byte> palette, ReadOnlySpan<byte> transparency, bool hasFilterBytes)
    {
        int rowStride = hasFilterBytes ? 1 + sourceStride : sourceStride;
        byte* output = (byte*)NativeMemory.Alloc((nuint)(width * height * 4));
        try
        {
            switch (colorType)
            {
                case 0:
                    ConvertGrayscale(output, source, width, height, rowStride, bitDepth, transparency, hasFilterBytes);
                    break;
                case 2:
                    ConvertRGB(output, source, width, height, rowStride, bitDepth, transparency, hasFilterBytes);
                    break;
                case 3:
                    ConvertIndexed(output, source, width, height, rowStride, bitDepth, palette, transparency, hasFilterBytes);
                    break;
                case 4:
                    ConvertGrayscaleAlpha(output, source, width, height, rowStride, bitDepth, hasFilterBytes);
                    break;
                case 6:
                    ConvertRGBA(output, source, width, height, rowStride, bitDepth, hasFilterBytes);
                    break;
            }

            return output;
        }
        catch
        {
            NativeMemory.Free(output);
            throw;
        }
    }

    private static void ConvertGrayscale(
        byte* output, byte* source, int width, int height,
        int rowStride, int bitDepth, ReadOnlySpan<byte> transparency, bool hasFilterBytes)
    {
        int transparentGray = transparency.IsEmpty ? -1 : BinaryPrimitives.ReadUInt16BigEndian(transparency);

        int pixelOffset = hasFilterBytes ? 1 : 0;

        if (bitDepth == 8)
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * rowStride + pixelOffset;
                byte* dstRow = output + y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    byte gray = srcRow[x];
                    byte alpha = 0xFF;

                    if (gray == transparentGray)
                        alpha = 0x00;

                    dstRow[x * 4 + 0] = gray;
                    dstRow[x * 4 + 1] = gray;
                    dstRow[x * 4 + 2] = gray;
                    dstRow[x * 4 + 3] = alpha;
                }
            }
        }
        else if (bitDepth == 16)
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * rowStride + pixelOffset;
                byte* dstRow = output + y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    // Take high byte of 16-bit sample
                    byte gray = srcRow[x * 2];
                    byte alpha = 0xFF;

                    if (transparentGray >= 0 && transparentGray == ((srcRow[x * 2] << 8) | srcRow[x * 2 + 1]))
                        alpha = 0x00;

                    dstRow[x * 4 + 0] = gray;
                    dstRow[x * 4 + 1] = gray;
                    dstRow[x * 4 + 2] = gray;
                    dstRow[x * 4 + 3] = alpha;
                }
            }
        }
        else
        {
            // Sub-byte depths: 1, 2, 4
            int mask = (1 << bitDepth) - 1;

            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * rowStride + pixelOffset;
                byte* dstRow = output + y * width * 4;

                int bitPos = 0;
                int byteIdx = 0;

                for (int x = 0; x < width; x++)
                {
                    int shift = 8 - bitDepth - bitPos;
                    int sample = (srcRow[byteIdx] >> shift) & mask;
                    byte gray = ScaleToByte(sample, bitDepth);

                    dstRow[x * 4 + 0] = gray;
                    dstRow[x * 4 + 1] = gray;
                    dstRow[x * 4 + 2] = gray;
                    dstRow[x * 4 + 3] = sample == transparentGray ? (byte)0 : (byte)0xFF;

                    bitPos += bitDepth;
                    if (bitPos == 8)
                    {
                        bitPos = 0;
                        byteIdx++;
                    }
                }
            }
        }
    }

    private static void ConvertRGB(
        byte* output, byte* source, int width, int height,
        int rowStride, int bitDepth, ReadOnlySpan<byte> transparency, bool hasFilterBytes)
    {
        bool hasTRNS = !transparency.IsEmpty;
        ushort tRNSR = 0, tRNSG = 0, tRNSB = 0;
        if (hasTRNS)
        {
            tRNSR = BinaryPrimitives.ReadUInt16BigEndian(transparency);
            tRNSG = BinaryPrimitives.ReadUInt16BigEndian(transparency[2..]);
            tRNSB = BinaryPrimitives.ReadUInt16BigEndian(transparency[4..]);
        }

        int pixelOffset = hasFilterBytes ? 1 : 0;

        if (bitDepth == 8)
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * rowStride + pixelOffset;
                byte* dstRow = output + y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    byte r = srcRow[x * 3 + 0];
                    byte g = srcRow[x * 3 + 1];
                    byte b = srcRow[x * 3 + 2];
                    byte alpha = 0xFF;

                    if (hasTRNS && r == tRNSR && g == tRNSG && b == tRNSB)
                        alpha = 0x00;

                    dstRow[x * 4 + 0] = r;
                    dstRow[x * 4 + 1] = g;
                    dstRow[x * 4 + 2] = b;
                    dstRow[x * 4 + 3] = alpha;
                }
            }
        }
        else // bitDepth == 16
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * rowStride + pixelOffset;
                byte* dstRow = output + y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    byte r = srcRow[x * 6 + 0];
                    byte g = srcRow[x * 6 + 2];
                    byte b = srcRow[x * 6 + 4];
                    byte alpha = 0xFF;

                    if (hasTRNS)
                    {
                        ushort sr = (ushort)((srcRow[x * 6 + 0] << 8) | srcRow[x * 6 + 1]);
                        ushort sg = (ushort)((srcRow[x * 6 + 2] << 8) | srcRow[x * 6 + 3]);
                        ushort sb = (ushort)((srcRow[x * 6 + 4] << 8) | srcRow[x * 6 + 5]);

                        if (sr == tRNSR && sg == tRNSG && sb == tRNSB)
                            alpha = 0x00;
                    }

                    dstRow[x * 4 + 0] = r;
                    dstRow[x * 4 + 1] = g;
                    dstRow[x * 4 + 2] = b;
                    dstRow[x * 4 + 3] = alpha;
                }
            }
        }
    }

    private static void ConvertIndexed(
        byte* output, byte* source, int width, int height,
        int rowStride, int bitDepth, ReadOnlySpan<byte> palette, ReadOnlySpan<byte> transparency, bool hasFilterBytes)
    {
        int mask = (1 << bitDepth) - 1;
        int pixelOffset = hasFilterBytes ? 1 : 0;

        for (int y = 0; y < height; y++)
        {
            byte* srcRow = source + y * rowStride + pixelOffset;
            byte* dstRow = output + y * width * 4;

            int bitPos = 0;
            int byteIdx = 0;

            for (int x = 0; x < width; x++)
            {
                int shift = 8 - bitDepth - bitPos;
                int index = (srcRow[byteIdx] >> shift) & mask;
                int maxIndex = palette.Length / 3 - 1;
                if (index > maxIndex)
                    index = maxIndex;

                byte r = palette[index * 3 + 0];
                byte g = palette[index * 3 + 1];
                byte b = palette[index * 3 + 2];
                byte a = (index < transparency.Length)
                    ? transparency[index]
                    : (byte)0xFF;

                dstRow[x * 4 + 0] = r;
                dstRow[x * 4 + 1] = g;
                dstRow[x * 4 + 2] = b;
                dstRow[x * 4 + 3] = a;

                bitPos += bitDepth;
                if (bitPos == 8)
                {
                    bitPos = 0;
                    byteIdx++;
                }
            }
        }
    }

    private static void ConvertGrayscaleAlpha(
        byte* output, byte* source, int width, int height,
        int rowStride, int bitDepth, bool hasFilterBytes)
    {
        int pixelOffset = hasFilterBytes ? 1 : 0;

        if (bitDepth == 8)
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * rowStride + pixelOffset;
                byte* dstRow = output + y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    byte gray = srcRow[x * 2 + 0];
                    byte alpha = srcRow[x * 2 + 1];

                    dstRow[x * 4 + 0] = gray;
                    dstRow[x * 4 + 1] = gray;
                    dstRow[x * 4 + 2] = gray;
                    dstRow[x * 4 + 3] = alpha;
                }
            }
        }
        else // bitDepth == 16
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * rowStride + pixelOffset;
                byte* dstRow = output + y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    byte gray = srcRow[x * 4 + 0]; // high byte
                    byte alpha = srcRow[x * 4 + 2]; // high byte of alpha

                    dstRow[x * 4 + 0] = gray;
                    dstRow[x * 4 + 1] = gray;
                    dstRow[x * 4 + 2] = gray;
                    dstRow[x * 4 + 3] = alpha;
                }
            }
        }
    }

    private static void ConvertRGBA(
        byte* output, byte* source, int width, int height,
        int rowStride, int bitDepth, bool hasFilterBytes)
    {
        int pixelOffset = hasFilterBytes ? 1 : 0;

        if (bitDepth == 8)
        {
            // Copy row by row, skipping filter bytes if present
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * rowStride + pixelOffset;
                byte* dstRow = output + y * width * 4;
                int copyBytes = width * 4;
                Buffer.MemoryCopy(srcRow, dstRow, copyBytes, copyBytes);
            }
        }
        else // bitDepth == 16
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * rowStride + pixelOffset;
                byte* dstRow = output + y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    dstRow[x * 4 + 0] = srcRow[x * 8 + 0]; // R high byte
                    dstRow[x * 4 + 1] = srcRow[x * 8 + 2]; // G high byte
                    dstRow[x * 4 + 2] = srcRow[x * 8 + 4]; // B high byte
                    dstRow[x * 4 + 3] = srcRow[x * 8 + 6]; // A high byte
                }
            }
        }
    }

    /// <summary>
    /// Scale a sub-byte sample value to 0-255 range.
    /// </summary>
    private static byte ScaleToByte(int sample, int bitDepth)
    {
        return bitDepth switch
        {
            1 => (byte)(sample * 255),          // 0->0, 1->255
            2 => (byte)(sample * 85),            // 0->0, 1->85, 2->170, 3->255
            4 => (byte)(sample * 17),            // 0->0, 15->255
            _ => (byte)sample
        };
    }

    #endregion
}
