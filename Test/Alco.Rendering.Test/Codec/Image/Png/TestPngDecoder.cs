using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

/// <summary>Tests exact PNG pixel output and rejection of malformed inputs.</summary>
[TestFixture]
public unsafe class TestPngDecoder
{
    /// <summary>Checks every RGBA byte against an independent PngSuite reference.</summary>
    [TestCaseSource(nameof(PngSuiteReferences))]
    public void Decode_PngSuitePixels(string filename, string expectedHash)
    {
        AssertDecoded(ReadPngSuiteFile(filename), 32, 32, pixels =>
            Assert.That(Convert.ToHexString(SHA256.HashData(pixels)), Is.EqualTo(expectedHash)));
    }

    /// <summary>Checks each filter on the first and later rows of tightly packed RGBA8.</summary>
    [Test]
    public void Decode_Rgba8Filters(
        [Values(1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17)] int width,
        [Values(0, 1, 2, 3, 4)] int firstFilter)
    {
        // The rows straddle 16-, 32- and 64-byte boundaries. The height exceeds the widest
        // row in bytes so scanline compaction encounters both overlap and non-overlap.
        const int height = 71;
        byte[] expected = new byte[width * height * 4];
        new Random(0x504E47).NextBytes(expected);
        expected[0] = 237;
        expected[3] = 0;
        expected[7] = 91;
        byte[] scanlines = FilterRgbaRows(expected, width, firstFilter);

        AssertPixels(CreatePng(width, height, ("IDAT", CompressZlib(scanlines))), width, height, expected);
    }

    /// <summary>Checks split IDAT streams, empty chunks, and unused bytes in the final chunk.</summary>
    [Test]
    public void Decode_IdatBoundaries([Values] bool split, [Values] bool trailingBytes)
    {
        byte[] expected = [9, 38, 76, 0, 255, 103, 17, 99, 19, 47, 102, 249, 112, 241, 7, 255];
        byte[] zlib = CompressZlib(FilterRgbaRows(expected, 2, firstFilter: 2));
        byte[] finalIdat = split ? zlib[^2..] : zlib;
        if (trailingBytes)
            finalIdat = [..finalIdat, 0xDE, 0xAD, 0xBE, 0xEF, 0x42];

        // Split the zlib header, DEFLATE data, and Adler-32 footer at distinct chunk boundaries.
        (string Type, byte[] Data)[] chunks = split
            ? [
                ("IDAT", []), ("IDAT", zlib[..1]), ("IDAT", []),
                ("IDAT", zlib[1..4]), ("IDAT", zlib[4..^2]), ("IDAT", finalIdat),
            ]
            : [("IDAT", finalIdat)];
        if (split && !trailingBytes)
            chunks = [..chunks, ("IDAT", [])];

        AssertPixels(CreatePng(2, 2, chunks), 2, 2, expected);
    }

    /// <summary>Checks packed palette indices, partial transparency, and empty Adam7 passes.</summary>
    [Test]
    public void Decode_IndexedTransparency([Values] bool interlaced)
    {
        // 2-bit indices by output row: 0,1,2 / 3,2,1 / 1,0,3.
        byte[] scanlines = interlaced
            ? [0, 0x00, 2, 0x80, 2, 0x70, 2, 0x40, 2, 0xC0, 2, 0xE4] // Passes 1, 4, 5, 6 (two rows), 7.
            : [0, 0x1B, 0, 0xE7, 0, 0x4F]; // Nonzero padding bits must be ignored.
        byte[] expected =
        [
            17, 41, 73, 0,   101, 131, 151, 127,   181, 191, 211, 255,
            221, 231, 241, 255,   181, 191, 211, 255,   101, 131, 151, 127,
            101, 131, 151, 127,   17, 41, 73, 0,   221, 231, 241, 255,
        ];
        byte[] png = CreatePng(3, 3, 2, 3, interlaced,
            ("PLTE", [17, 41, 73, 101, 131, 151, 181, 191, 211, 221, 231, 241]),
            ("tRNS", [0, 127]),
            ("IDAT", CompressZlib(scanlines)));

        AssertPixels(png, 3, 3, expected);
    }

    /// <summary>Checks that an empty transparency table leaves all palette entries opaque.</summary>
    [Test]
    public void Decode_EmptyIndexedTransparency()
    {
        byte[] png = CreatePng(1, 1, 1, 3, false,
            ("PLTE", [17, 41, 73]), ("tRNS", []), ("IDAT", CompressZlib([0, 0])));

        AssertPixels(png, 1, 1, [17, 41, 73, 255]);
    }

    /// <summary>Checks that packed grayscale transparency compares the unscaled sample.</summary>
    [Test]
    public void Decode_PackedGrayscaleTransparency([Values] bool interlaced)
    {
        byte[] scanlines = interlaced ? [0, 0, 0, 0x80, 0, 0x40] : [0, 0x18];
        byte[] png = CreatePng(3, 1, 2, 0, interlaced, ("tRNS", [0, 1]), ("IDAT", CompressZlib(scanlines)));

        AssertPixels(png, 3, 1, [0, 0, 0, 255, 85, 85, 85, 0, 170, 170, 170, 255]);
    }

    /// <summary>Checks that 16-bit transparency matches full samples before stripping low bytes.</summary>
    [Test]
    public void Decode_Rgb16Transparency()
    {
        byte[] colorKey = [0x11, 0xFF, 0x80, 0x80, 0xE3, 0xFF];
        byte[] scanline = [0, ..colorKey, 0x11, 0xFE, 0x80, 0x80, 0xE3, 0xFF];
        byte[] png = CreatePng(2, 1, 16, 2, false, ("tRNS", colorKey), ("IDAT", CompressZlib(scanline)));

        AssertPixels(png, 2, 1, [0x11, 0x80, 0xE3, 0, 0x11, 0x80, 0xE3, 255]);
    }

    /// <summary>Checks CRC rejection even for ignored and empty chunks.</summary>
    [TestCase("IHDR", 0)]
    [TestCase("tEXt", 0)]
    [TestCase("IDAT", 0)]
    [TestCase("IDAT", 1)]
    [TestCase("IEND", 0)]
    public void Decode_RejectsBadChunkCrc(string type, int occurrence)
    {
        byte[] png = CreatePng(1, 1, ("tEXt", "Title\0CRC fixture"u8.ToArray()),
            ("IDAT", []), ("IDAT", CompressZlib([0, 1, 2, 3, 4])));
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        for (int offset = 8; offset < png.Length;)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset));
            int crcOffset = offset + 8 + length;
            if (png.AsSpan(offset + 4, 4).SequenceEqual(typeBytes) && occurrence-- == 0)
            {
                png[crcOffset] ^= 1;
                AssertRejected(png);
                return;
            }

            offset = crcOffset + 4;
        }

        Assert.Fail($"Fixture does not contain the requested {type} chunk.");
    }

    /// <summary>Checks rejection of invalid zlib header and DEFLATE fields with a valid PNG CRC.</summary>
    [TestCase(1, 0x01, TestName = "Decode_RejectsBadZlibHeaderCheck")]
    [TestCase(2, 0x06, TestName = "Decode_RejectsReservedDeflateBlockType")]
    public void Decode_RejectsMalformedZlib(int offset, byte mask)
    {
        // NoCompression gives a stored block, so setting both BTYPE bits makes it reserved.
        byte[] zlib = CompressZlib([0, 1, 2, 3, 4], CompressionLevel.NoCompression);
        zlib[offset] ^= mask;
        AssertRejected(CreatePng(1, 1, ("IDAT", zlib)));
    }

    /// <summary>Checks rejection of preset dictionaries, which PNG forbids.</summary>
    [Test]
    public void Decode_RejectsZlibPresetDictionary()
    {
        // zlib-compressed bytes 0..4, using bytes 0..31 as a preset dictionary.
        byte[] zlib = [0x78, 0xBB, 0x15, 0x70, 0x01, 0xF1, 0x63, 0x00, 0xC9, 0x03, 0x00, 0x00, 0x19, 0x00, 0x0B];
        AssertRejected(CreatePng(1, 1, ("IDAT", zlib)));
    }

    /// <summary>Checks that the Adler-32 verification uses the footer at the DEFLATE end.</summary>
    [Test]
    public void Decode_RejectsBadAdler32([Values] bool trailingBytes)
    {
        byte[] zlib = CompressZlib([0, 1, 2, 3, 4]);
        byte[] correctAdler = zlib[^4..];
        zlib[^1] ^= 1;
        if (trailingBytes)
            zlib = [..zlib, ..correctAdler];

        AssertRejected(CreatePng(1, 1, ("IDAT", zlib)));
    }

    /// <summary>Checks rejection of a zlib stream that ends partway through its checksum.</summary>
    [Test]
    public void Decode_RejectsTruncatedAdler32()
    {
        byte[] zlib = CompressZlib([0, 1, 2, 3, 4]);
        AssertRejected(CreatePng(1, 1, ("IDAT", zlib[..^1])));
    }

    /// <summary>Checks rejection of a valid zlib stream with too few or too many scanline bytes.</summary>
    [TestCase(false, 17)]
    [TestCase(false, 19)]
    [TestCase(true, 18)]
    [TestCase(true, 20)]
    public void Decode_RejectsWrongScanlineLength(bool interlaced, int byteCount)
    {
        // 2x2 RGBA8 requires 18 bytes normally, or 19 across Adam7 passes 1, 6 and 7.
        byte[] zlib = CompressZlib(new byte[byteCount]);
        AssertRejected(CreatePng(2, 2, 8, 6, interlaced, ("IDAT", zlib)));
    }

    /// <summary>Checks filter validation on first and later scanlines across decode paths.</summary>
    [Test]
    public void Decode_RejectsInvalidFilter([Values(2, 6)] int colorType, [Values] bool interlaced, [Values(0, 3)] int scanline)
    {
        // A 1x4 Adam7 image has four one-pixel scanlines; the last is the second row of pass 7.
        int rowSize = colorType == 6 ? 5 : 4;
        byte[] scanlines = new byte[4 * rowSize];
        scanlines[scanline * rowSize] = 5;
        AssertRejected(CreatePng(1, 4, 8, (byte)colorType, interlaced, ("IDAT", CompressZlib(scanlines))));
    }

    /// <summary>Checks rejection when the RGBA output, source stride, or scanline buffer would overflow.</summary>
    [TestCase(32_768, 32_768, 1, 0, "dimensions", TestName = "Decode_RejectsRgbaOutputSizeOverflow")]
    [TestCase(1 << 28, 1, 16, 6, "buffer size", TestName = "Decode_RejectsSourceStrideOverflow")]
    [TestCase(1 << 27, 2, 16, 6, "buffer size", TestName = "Decode_RejectsScanlineBufferSizeOverflow")]
    public void Decode_RejectsImageSizeOverflow(int width, int height, byte bitDepth, byte colorType, string diagnostic)
    {
        // These IDATs are tiny. Require a size error, not an error about the short inflated data.
        byte[] png = CreatePng(width, height, bitDepth, colorType, false, ("IDAT", CompressZlib([0, 0])));
        Assert.That(AssertRejected(png).Message, Does.Contain(diagnostic));
    }

    /// <summary>Checks that corrupt PngSuite files fail with the public decode exception.</summary>
    [TestCase("xc1n0g08.png")]
    [TestCase("xc9n2c08.png")]
    [TestCase("xcrn0g04.png")]
    [TestCase("xd0n2c08.png")]
    [TestCase("xdtn0g01.png")]
    public void Decode_RejectsCorruptPngSuiteFile(string filename) => AssertRejected(ReadPngSuiteFile(filename));

    private static IEnumerable<TestCaseData> PngSuiteReferences()
    {
        // Offline Pillow SHA-256 of top-down, straight RGBA8 samples without gamma correction.
        // All 16-bit channels keep the high byte; Pillow's I;16 grayscale was shifted explicitly.
        (string Suffix, string Hash)[] basicImages =
        [
            ("0g01", "661985E83F94A569510DED43E65EDB11F4CED1121C611209F7ABE9A9C40C71A8"),
            ("0g02", "166BD68377B119B5E93E73EF554E35DE7471BDD2FC3BC2070F0F7BD5BE82AE97"),
            ("0g04", "B05A4BC8E7079C8AA0E491086CCB156DD4BDBC67E57BB8C9D803D7E75778DA9E"),
            ("0g08", "982FAA277E83F73CA15B491E67EB41FA25526418ED23E057A9986C4F620EB158"),
            ("0g16", "5F42DF4FD50DBEA319BD9A4C26F7D5E37CE60F71FA35C28039ABCD812609A6BC"),
            ("2c08", "23A53C674EC50D5A5EB9C3F679B6B19BA5304AE99DFF76801BEC4939E0F0C99E"),
            ("2c16", "7C4B73E829F02793549B4480E25F0C0B332ABCB24AC059DBAD855FD1D726C17A"),
            ("3p01", "614996FEB597F62B913614A57BE5CE64EEA97EFC57CD55BBBA535D2F61716833"),
            ("3p02", "A383497791948D8B7AE8F9158FB7B4E9FEAD4693814EE758A97BC426DC9A27CF"),
            ("3p04", "A7ABC212CF1A44C85DF377773F3722DC118F0C4159DF89FDAC2DFE6911ABE378"),
            ("3p08", "B1C3302ECEAE6738C36EDAFA98C8054824D9440F3BA53A3F17CC81D29ACC32CC"),
            ("4a08", "76B94A71D3C183A362C2CF6A46EBB50ADC9D3A25A89BC0AFC46FDA6DBB002509"),
            ("4a16", "E96F0631F384C4542828119375D5836CF263ACE118100FD4FF0C5D3624D2C454"),
            ("6a08", "2EB6A2CB3166E9C188ADD371157E9F81CAA18FDF34D218844ED930B53B7431D2"),
            ("6a16", "F6912D034804DC6B009AFEA0108CD07B524F79AC84D670F92CE077EEC63BEAD7"),
        ];
        foreach (var (suffix, hash) in basicImages)
        {
            yield return new TestCaseData($"basn{suffix}.png", hash).SetName($"{{m}}(basn{suffix}.png)");
            yield return new TestCaseData($"basi{suffix}.png", hash).SetName($"{{m}}(basi{suffix}.png)");
        }

        const string compressionHash = "A9DFF6085FE81EEA37100681E299A0504206137521DC59D592D87FA73B18C917";
        foreach (string filename in new[] { "z00n2c08.png", "z03n2c08.png", "z06n2c08.png", "z09n2c08.png" })
            yield return new TestCaseData(filename, compressionHash).SetName($"{{m}}({filename})");
    }

    private static byte[] ReadPngSuiteFile(string filename) => File.ReadAllBytes(Path.Combine("Files", "Image", "Png", filename));

    private static void AssertDecoded(byte[] png, int width, int height, Action<ReadOnlySpan<byte>> assertPixels)
    {
        byte* pixels = ImageDecodeUtility.DecodePng(png, out int actualWidth, out int actualHeight);
        try
        {
            Assert.That(pixels != null, Is.True, "Decoder returned no pixel buffer.");
            Assert.That(actualWidth, Is.EqualTo(width));
            Assert.That(actualHeight, Is.EqualTo(height));
            assertPixels(new ReadOnlySpan<byte>(pixels, width * height * 4));
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    private static void AssertPixels(byte[] png, int width, int height, byte[] expected) =>
        AssertDecoded(png, width, height, pixels =>
        {
            Assert.That(pixels.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i] == expected[i])
                    continue;

                int pixel = i / 4;
                char channel = "RGBA"[i % 4];
                Assert.Fail($"Pixel ({pixel % width}, {pixel / width}), channel {channel}: expected {expected[i]}, got {pixels[i]}.");
            }
        });

    private static ImageDecodeException AssertRejected(byte[] png) =>
        Assert.Throws<ImageDecodeException>(() => NativeMemory.Free(ImageDecodeUtility.DecodePng(png, out _, out _)))!;

    // Apply forward filters to original pixels rather than relying on the engine's PNG encoder.
    private static byte[] FilterRgbaRows(byte[] pixels, int width, int firstFilter)
    {
        const int channels = 4;
        int stride = width * channels;
        int height = pixels.Length / stride;
        byte[] scanlines = new byte[height * (stride + 1)];
        for (int y = 0; y < height; y++)
        {
            int row = y * stride;
            int encodedRow = y * (stride + 1);
            int filter = (firstFilter + y) % 5;
            scanlines[encodedRow] = (byte)filter;
            for (int x = 0; x < stride; x++)
            {
                int left = x >= channels ? pixels[row + x - channels] : 0;
                int above = y > 0 ? pixels[row + x - stride] : 0;
                int upperLeft = y > 0 && x >= channels ? pixels[row + x - stride - channels] : 0;
                int predictor = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => above,
                    3 => (left + above) / 2,
                    _ => Paeth(left, above, upperLeft),
                };
                scanlines[encodedRow + x + 1] = unchecked((byte)(pixels[row + x] - predictor));
            }
        }

        return scanlines;
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int leftError = Math.Abs(estimate - left);
        int aboveError = Math.Abs(estimate - above);
        int upperLeftError = Math.Abs(estimate - upperLeft);
        if (leftError <= aboveError && leftError <= upperLeftError)
            return left;

        return aboveError <= upperLeftError ? above : upperLeft;
    }

    private static byte[] CompressZlib(ReadOnlySpan<byte> scanlines, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, level, leaveOpen: true))
            zlib.Write(scanlines);

        return output.ToArray();
    }

    private static byte[] CreatePng(int width, int height, params (string Type, byte[] Data)[] chunks) =>
        CreatePng(width, height, 8, 6, false, chunks);

    private static byte[] CreatePng(int width, int height, byte bitDepth, byte colorType, bool interlaced,
        params (string Type, byte[] Data)[] chunks)
    {
        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = bitDepth;
        ihdr[9] = colorType;
        ihdr[12] = interlaced ? (byte)1 : (byte)0;
        WriteChunk(png, "IHDR"u8, ihdr);
        foreach (var (type, data) in chunks)
            WriteChunk(png, Encoding.ASCII.GetBytes(type), data);

        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WriteChunk(MemoryStream png, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        png.Write(word);
        int typeOffset = (int)png.Position;
        png.Write(type);
        png.Write(data);
        uint crc = Crc32.HashToUInt32(png.GetBuffer().AsSpan(typeOffset, 4 + data.Length));
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        png.Write(word);
    }
}
