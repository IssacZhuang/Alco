using System.Runtime.InteropServices;

namespace Alco.Rendering.Codec.Image;

/// <summary>
/// Decodes PNG image data to RGBA8 pixel buffer.
/// Orchestrates chunk parsing, zlib decompression, row defiltering, Adam7 deinterlacing,
/// and color type conversion.
/// </summary>
internal static unsafe class PngDecoder
{
    /// <summary>PNG signature bytes: 89 50 4E 47 0D 0A 1A 0A</summary>
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
        ValidateSignature(data);

        width = 0;
        height = 0;
        int bitDepth = 0;
        int colorType = 0;
        int interlace = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        byte[]? combinedIdat = null;
        int idatLength = 0;
        bool ihdrSeen = false;

        int pos = 8; // skip signature

        // Parse chunks
        while (pos < data.Length)
        {
            if (pos + 8 > data.Length)
                throw new ImageDecodeException("Truncated chunk header.");

            int chunkLength = ReadBigEndianInt32(data.Slice(pos, 4));
            pos += 4;

            uint chunkType = ReadBigEndianUInt32(data.Slice(pos, 4));
            pos += 4;

            if (pos + chunkLength + 4 > data.Length)
                throw new ImageDecodeException($"Truncated chunk data for type 0x{chunkType:X8}.");

            ReadOnlySpan<byte> chunkData = data.Slice(pos, chunkLength);
            pos += chunkLength;
            pos += 4; // skip CRC

            switch (chunkType)
            {
                case 0x49484452u: // IHDR
                    if (ihdrSeen)
                        throw new ImageDecodeException("Duplicate IHDR chunk.");

                    if (chunkLength != 13)
                        throw new ImageDecodeException($"Invalid IHDR chunk length: {chunkLength} (expected 13).");

                    ParseIHDR(chunkData, out width, out height, out bitDepth, out colorType, out interlace);
                    ihdrSeen = true;
                    break;

                case 0x504C5445u: // PLTE
                    if (!ihdrSeen)
                        throw new ImageDecodeException("PLTE chunk before IHDR.");

                    palette = chunkData.ToArray();
                    break;

                case 0x74524E53u: // tRNS
                    if (!ihdrSeen)
                        throw new ImageDecodeException("tRNS chunk before IHDR.");

                    transparency = chunkData.ToArray();
                    break;

                case 0x49444154u: // IDAT
                    if (!ihdrSeen)
                        throw new ImageDecodeException("IDAT chunk before IHDR.");

                    if (combinedIdat == null)
                    {
                        combinedIdat = chunkData.ToArray();
                        idatLength = chunkLength;
                    }
                    else
                    {
                        Array.Resize(ref combinedIdat, idatLength + chunkLength);
                        chunkData.CopyTo(combinedIdat.AsSpan(idatLength));
                        idatLength += chunkLength;
                    }
                    break;

                case 0x49454E44u: // IEND
                    goto DoneParsing;

                default:
                    // Unknown chunk: already skipped
                    break;
            }
        }

    DoneParsing:

        if (!ihdrSeen)
            throw new ImageDecodeException("No IHDR chunk found.");

        if (combinedIdat == null)
            throw new ImageDecodeException("No IDAT chunk found.");

        if (colorType == 3 && palette == null)
            throw new ImageDecodeException("Indexed color type (3) requires PLTE chunk.");

        // Validate output size with checked arithmetic
        nuint outputSize;
        try
        {
            outputSize = checked((nuint)width * (nuint)height * 4);
        }
        catch (OverflowException)
        {
            throw new ImageDecodeException($"Image dimensions overflow: {width}x{height} RGBA8.");
        }

        // Allocate RGBA8 output buffer
        byte* output = (byte*)NativeMemory.Alloc(outputSize);

        try
        {
            // Calculate bytes-per-pixel and stride for the source format
            int bytesPerPixel = GetBytesPerPixel(bitDepth, colorType);
            int stride = GetStride(width, bitDepth, colorType);

            if (interlace == 0)
            {
                DecodeNonInterlaced(combinedIdat.AsSpan(0, idatLength), output,
                    width, height, bitDepth, colorType, bytesPerPixel, stride,
                    palette, transparency);
            }
            else
            {
                DecodeInterlaced(combinedIdat.AsSpan(0, idatLength), output,
                    width, height, bitDepth, colorType, bytesPerPixel, stride,
                    palette, transparency);
            }

            return output;
        }
        catch
        {
            NativeMemory.Free(output);
            throw;
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
        ValidateSignature(data);

        if (data.Length < 8 + 8 + 13 + 4)
            throw new ImageDecodeException("PNG data too short for IHDR chunk.");

        // Read first chunk header
        int chunkLength = ReadBigEndianInt32(data.Slice(8, 4));
        uint chunkType = ReadBigEndianUInt32(data.Slice(12, 4));

        if (chunkType != 0x49484452u) // IHDR
            throw new ImageDecodeException("First chunk is not IHDR.");

        if (chunkLength != 13)
            throw new ImageDecodeException($"Invalid IHDR chunk length: {chunkLength}.");

        ReadOnlySpan<byte> ihdrData = data.Slice(16, 13);
        int width = ReadBigEndianInt32(ihdrData.Slice(0, 4));
        int height = ReadBigEndianInt32(ihdrData.Slice(4, 4));

        if (width <= 0 || height <= 0)
            throw new ImageDecodeException($"Invalid PNG dimensions: {width}x{height}.");

        return (width, height);
    }

    #region Chunk Parsing Helpers

    private static void ValidateSignature(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data.Slice(0, 8).SequenceEqual(PngSignature))
            throw new ImageDecodeException("Invalid PNG signature.");
    }

    private static void ParseIHDR(ReadOnlySpan<byte> ihdr, out int width, out int height,
        out int bitDepth, out int colorType, out int interlace)
    {
        width = ReadBigEndianInt32(ihdr.Slice(0, 4));
        height = ReadBigEndianInt32(ihdr.Slice(4, 4));
        bitDepth = ihdr[8];
        colorType = ihdr[9];
        int compression = ihdr[10];
        int filter = ihdr[11];
        interlace = ihdr[12];

        if (width <= 0 || height <= 0)
            throw new ImageDecodeException($"Invalid PNG dimensions: {width}x{height}.");

        if (bitDepth is not (1 or 2 or 4 or 8 or 16))
            throw new ImageDecodeException($"Unsupported bit depth: {bitDepth}.");

        if (colorType is not (0 or 2 or 3 or 4 or 6))
            throw new ImageDecodeException($"Unsupported color type: {colorType}.");

        // Validate bit depth / color type combinations
        if (colorType == 3 && bitDepth is not (1 or 2 or 4 or 8))
            throw new ImageDecodeException($"Invalid bit depth {bitDepth} for indexed color type (3).");

        if ((colorType == 0 || colorType == 4) && bitDepth is not (1 or 2 or 4 or 8 or 16))
            throw new ImageDecodeException($"Invalid bit depth {bitDepth} for grayscale color type ({colorType}).");

        if ((colorType == 2 || colorType == 6) && bitDepth is not (8 or 16))
            throw new ImageDecodeException($"Invalid bit depth {bitDepth} for color type {colorType}.");

        if (compression != 0)
            throw new ImageDecodeException($"Unsupported compression method: {compression}.");

        if (filter != 0)
            throw new ImageDecodeException($"Unsupported filter method: {filter}.");

        if (interlace is not (0 or 1))
            throw new ImageDecodeException($"Unsupported interlace method: {interlace}.");
    }

    private static int ReadBigEndianInt32(ReadOnlySpan<byte> bytes)
    {
        return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    }

    private static uint ReadBigEndianUInt32(ReadOnlySpan<byte> bytes)
    {
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
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
            0 => bitDepth >= 8 ? Math.DivRem(bitDepth, 8, out _) : 1, // grayscale
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

        return (width * bitsPerPixel + 7) / 8;
    }

    #endregion

    #region Non-interlaced Decode

    private static void DecodeNonInterlaced(
        ReadOnlySpan<byte> idatData, byte* output,
        int width, int height, int bitDepth, int colorType,
        int bytesPerPixel, int stride,
        byte[]? palette, byte[]? transparency)
    {
        int decompressedSize = height * (1 + stride);
        byte* decompressed = (byte*)NativeMemory.Alloc((nuint)decompressedSize);

        try
        {
            Span<byte> decompressedSpan = new(decompressed, decompressedSize);
            PngZlib.DecompressZlib(idatData, decompressedSpan);

            // Defilter in-place
            PngDefilter.Defilter(decompressedSpan, width, height, bytesPerPixel);

            // Convert source format to RGBA8
            ConvertToRGBA8(output, decompressed, width, height, stride,
                bitDepth, colorType, palette, transparency);
        }
        finally
        {
            NativeMemory.Free(decompressed);
        }
    }

    #endregion

    #region Interlaced (Adam7) Decode

    private static void DecodeInterlaced(
        ReadOnlySpan<byte> idatData, byte* output,
        int width, int height, int bitDepth, int colorType,
        int bytesPerPixel, int stride,
        byte[]? palette, byte[]? transparency)
    {
        // Calculate total decompressed size across all passes
        int totalSize = 0;
        int[] passSizes = new int[7];
        int[] passStrides = new int[7];
        (int PassWidth, int PassHeight)[] passDims = new (int, int)[7];

        for (int pass = 0; pass < 7; pass++)
        {
            var (pw, ph) = PngAdam7.GetPassSize(pass, width, height);
            passDims[pass] = (pw, ph);

            if (pw == 0 || ph == 0)
            {
                passSizes[pass] = 0;
                passStrides[pass] = 0;
                continue;
            }

            int passStride = GetStride(pw, bitDepth, colorType);
            passStrides[pass] = passStride;
            int passSize = ph * (1 + passStride);
            passSizes[pass] = passSize;
            totalSize += passSize;
        }

        byte* decompressed = (byte*)NativeMemory.Alloc((nuint)totalSize);

        try
        {
            Span<byte> decompressedSpan = new(decompressed, totalSize);
            PngZlib.DecompressZlib(idatData, decompressedSpan);

            // Allocate a temporary source-format buffer to merge all passes into
            int sourceStride = stride;
            nuint sourceBufferSize = (nuint)(height * sourceStride);
            byte* sourceBuffer = (byte*)NativeMemory.Alloc(sourceBufferSize);

            try
            {
                // Initialize source buffer to zero
                NativeMemory.Fill(sourceBuffer, sourceBufferSize, 0);

                int offset = 0;
                for (int pass = 0; pass < 7; pass++)
                {
                    var (pw, ph) = passDims[pass];
                    if (pw == 0 || ph == 0)
                        continue;

                    int passSize = passSizes[pass];
                    int passStride = passStrides[pass];

                    Span<byte> passData = decompressedSpan.Slice(offset, passSize);

                    // Defilter this pass in-place
                    PngDefilter.Defilter(passData, pw, ph, bytesPerPixel);

                    // Merge into source buffer
                    PngAdam7.MergePass(sourceBuffer, sourceStride,
                        passData, 1 + passStride,
                        pass, width, height, bytesPerPixel);

                    offset += passSize;
                }

                // Convert the merged source buffer to RGBA8
                ConvertToRGBA8(output, sourceBuffer, width, height, sourceStride,
                    bitDepth, colorType, palette, transparency);
            }
            finally
            {
                NativeMemory.Free(sourceBuffer);
            }
        }
        finally
        {
            NativeMemory.Free(decompressed);
        }
    }

    #endregion

    #region Color Type Conversion

    /// <summary>
    /// Convert source-format pixel data to RGBA8.
    /// For interlaced images, the source data is already deinterlaced into full-size image layout.
    /// </summary>
    private static void ConvertToRGBA8(
        byte* output, byte* source, int width, int height, int sourceStride,
        int bitDepth, int colorType, byte[]? palette, byte[]? transparency)
    {
        switch (colorType)
        {
            case 0: // Grayscale
                ConvertGrayscale(output, source, width, height, sourceStride, bitDepth, transparency);
                break;
            case 2: // RGB
                ConvertRGB(output, source, width, height, sourceStride, bitDepth, transparency);
                break;
            case 3: // Indexed
                ConvertIndexed(output, source, width, height, sourceStride, bitDepth, palette!, transparency);
                break;
            case 4: // Grayscale + Alpha
                ConvertGrayscaleAlpha(output, source, width, height, sourceStride, bitDepth);
                break;
            case 6: // RGBA
                ConvertRGBA(output, source, width, height, sourceStride, bitDepth);
                break;
        }
    }

    private static void ConvertGrayscale(
        byte* output, byte* source, int width, int height, int sourceStride,
        int bitDepth, byte[]? transparency)
    {
        ushort tRNSValue = 0;
        bool hasTRNS = transparency != null && transparency.Length >= 2;
        if (hasTRNS)
            tRNSValue = (ushort)((transparency![0] << 8) | transparency[1]);

        if (bitDepth == 8)
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * sourceStride;
                byte* dstRow = output + y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    byte gray = srcRow[x];
                    byte alpha = 0xFF;

                    if (hasTRNS && gray == (byte)tRNSValue)
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
                byte* srcRow = source + y * sourceStride;
                byte* dstRow = output + y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    // Take high byte of 16-bit sample
                    byte gray = srcRow[x * 2];
                    byte alpha = 0xFF;

                    if (hasTRNS && tRNSValue == ((srcRow[x * 2] << 8) | srcRow[x * 2 + 1]))
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
                byte* srcRow = source + y * sourceStride;
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
                    dstRow[x * 4 + 3] = 0xFF;

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
        byte* output, byte* source, int width, int height, int sourceStride,
        int bitDepth, byte[]? transparency)
    {
        bool hasTRNS = transparency != null && transparency.Length >= 6;
        ushort tRNSR = 0, tRNSG = 0, tRNSB = 0;
        if (hasTRNS)
        {
            tRNSR = (ushort)((transparency![0] << 8) | transparency[1]);
            tRNSG = (ushort)((transparency[2] << 8) | transparency[3]);
            tRNSB = (ushort)((transparency[4] << 8) | transparency[5]);
        }

        if (bitDepth == 8)
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * sourceStride;
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
                byte* srcRow = source + y * sourceStride;
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
        byte* output, byte* source, int width, int height, int sourceStride,
        int bitDepth, byte[] palette, byte[]? transparency)
    {
        int mask = (1 << bitDepth) - 1;

        for (int y = 0; y < height; y++)
        {
            byte* srcRow = source + y * sourceStride;
            byte* dstRow = output + y * width * 4;

            int bitPos = 0;
            int byteIdx = 0;

            for (int x = 0; x < width; x++)
            {
                int shift = 8 - bitDepth - bitPos;
                int index = (srcRow[byteIdx] >> shift) & mask;

                byte r = palette[index * 3 + 0];
                byte g = palette[index * 3 + 1];
                byte b = palette[index * 3 + 2];
                byte a = (transparency != null && index < transparency.Length)
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
        byte* output, byte* source, int width, int height, int sourceStride,
        int bitDepth)
    {
        if (bitDepth == 8)
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * sourceStride;
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
                byte* srcRow = source + y * sourceStride;
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
        byte* output, byte* source, int width, int height, int sourceStride,
        int bitDepth)
    {
        if (bitDepth == 8)
        {
            // Direct passthrough: source is already RGBA8
            long totalBytes = (long)width * height * 4;
            Buffer.MemoryCopy(source, output, totalBytes, totalBytes);
        }
        else // bitDepth == 16
        {
            for (int y = 0; y < height; y++)
            {
                byte* srcRow = source + y * sourceStride;
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
