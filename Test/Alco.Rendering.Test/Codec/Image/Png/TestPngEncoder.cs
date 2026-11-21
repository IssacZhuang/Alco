using System.Buffers.Binary;
using System.IO.Hashing;
using System.Runtime.InteropServices;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

/// <summary>Tests PNG encoding round-trips, validation and compression.</summary>
[TestFixture]
public unsafe class TestPngEncoder
{
    // ── Round-trip: encode RGBA8 → decode PNG → verify pixel-perfect ──

    /// <summary>Tests that a solid-color image round-trips pixel-perfect.</summary>
    [Test]
    public void RoundTrip_SolidColor()
    {
        // 4x3 image, all pixels = (255, 0, 128, 200)
        int w = 4, h = 3;
        byte[] pixels = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            pixels[i * 4 + 0] = 255; // R
            pixels[i * 4 + 1] = 0;   // G
            pixels[i * 4 + 2] = 128; // B
            pixels[i * 4 + 3] = 200; // A
        }

        byte[] png = ImageEncodeUtility.EncodePng(pixels, w, h);

        Assert.That(png, Is.Not.Null);
        Assert.That(png.Length, Is.GreaterThan(8));
        Assert.That(png[0..4], Is.EqualTo(new byte[] { 0x89, 0x50, 0x4E, 0x47 }));

        // Decode and verify
        byte* decoded = ImageDecodeUtility.DecodePng(png, out int dw, out int dh);
        try
        {
            Assert.That(dw, Is.EqualTo(w));
            Assert.That(dh, Is.EqualTo(h));

            for (int i = 0; i < w * h * 4; i++)
                Assert.That(decoded[i], Is.EqualTo(pixels[i]), $"Mismatch at byte {i} (pixel {i / 4}, channel {i % 4})");
        }
        finally
        {
            NativeMemory.Free(decoded);
        }
    }

    /// <summary>Tests that a gradient image round-trips pixel-perfect.</summary>
    [Test]
    public void RoundTrip_Gradient()
    {
        // 64x64 RGBA gradient image
        int w = 64, h = 64;
        byte[] pixels = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 4;
                pixels[idx + 0] = (byte)x;            // R: horizontal gradient
                pixels[idx + 1] = (byte)y;            // G: vertical gradient
                pixels[idx + 2] = (byte)(x ^ y);      // B: XOR pattern
                pixels[idx + 3] = (byte)(x + y);      // A: diagonal gradient
            }
        }

        byte[] png = ImageEncodeUtility.EncodePng(pixels, w, h);

        byte* decoded = ImageDecodeUtility.DecodePng(png, out int dw, out int dh);
        try
        {
            Assert.That(dw, Is.EqualTo(w));
            Assert.That(dh, Is.EqualTo(h));

            for (int i = 0; i < w * h * 4; i++)
                Assert.That(decoded[i], Is.EqualTo(pixels[i]), $"Mismatch at byte {i}");
        }
        finally
        {
            NativeMemory.Free(decoded);
        }
    }

    /// <summary>Tests that a 1x1 image round-trips.</summary>
    [Test]
    public void RoundTrip_1x1()
    {
        byte[] pixels = [100, 150, 200, 255];
        byte[] png = ImageEncodeUtility.EncodePng(pixels, 1, 1);

        byte* decoded = ImageDecodeUtility.DecodePng(png, out int w, out int h);
        try
        {
            Assert.That(w, Is.EqualTo(1));
            Assert.That(h, Is.EqualTo(1));
            Assert.That(decoded[0], Is.EqualTo(100));
            Assert.That(decoded[1], Is.EqualTo(150));
            Assert.That(decoded[2], Is.EqualTo(200));
            Assert.That(decoded[3], Is.EqualTo(255));
        }
        finally
        {
            NativeMemory.Free(decoded);
        }
    }

    /// <summary>Tests that a large random image round-trips pixel-perfect.</summary>
    [Test]
    public void RoundTrip_LargeImage()
    {
        // 256x256 random-ish pattern
        int w = 256, h = 256;
        byte[] pixels = new byte[w * h * 4];
        Random rng = new(42);
        rng.NextBytes(pixels);

        byte[] png = ImageEncodeUtility.EncodePng(pixels, w, h);

        byte* decoded = ImageDecodeUtility.DecodePng(png, out int dw, out int dh);
        try
        {
            Assert.That(dw, Is.EqualTo(w));
            Assert.That(dh, Is.EqualTo(h));

            for (int i = 0; i < w * h * 4; i++)
                Assert.That(decoded[i], Is.EqualTo(pixels[i]), $"Mismatch at byte {i}");
        }
        finally
        {
            NativeMemory.Free(decoded);
        }
    }

    /// <summary>Tests that an existing PNG file round-trips through decode and re-encode.</summary>
    [Test]
    public void RoundTrip_ExistingPngFile()
    {
        // Encode: decode an existing RGBA8 PNG, re-encode it, re-decode, verify identical
        string path = Path.Combine("Files", "Image", "Png", "basn6a08.png");
        byte[] fileData = File.ReadAllBytes(path);

        byte* original = ImageDecodeUtility.DecodePng(fileData, out int w, out int h);
        try
        {
            ReadOnlySpan<byte> originalSpan = new(original, w * h * 4);
            byte[] reEncoded = ImageEncodeUtility.EncodePng(originalSpan, w, h);

            byte* reDecoded = ImageDecodeUtility.DecodePng(reEncoded, out int dw, out int dh);
            try
            {
                Assert.That(dw, Is.EqualTo(w));
                Assert.That(dh, Is.EqualTo(h));

                for (int i = 0; i < w * h * 4; i++)
                    Assert.That(reDecoded[i], Is.EqualTo(original[i]), $"Mismatch at byte {i}");
            }
            finally
            {
                NativeMemory.Free(reDecoded);
            }
        }
        finally
        {
            NativeMemory.Free(original);
        }
    }

    // ── Pointer-based API ──

    /// <summary>Tests that encoding from a pointer produces a decodable PNG.</summary>
    [Test]
    public void EncodePng_FromPointer()
    {
        byte[] pixels = [10, 20, 30, 40, 50, 60, 70, 80];
        fixed (byte* ptr = pixels)
        {
            byte[] png = ImageEncodeUtility.EncodePng(ptr, 2, 1);

            byte* decoded = ImageDecodeUtility.DecodePng(png, out int w, out int h);
            try
            {
                Assert.That(w, Is.EqualTo(2));
                Assert.That(h, Is.EqualTo(1));
                Assert.That(decoded[0], Is.EqualTo(10));
                Assert.That(decoded[4], Is.EqualTo(50));
            }
            finally
            {
                NativeMemory.Free(decoded);
            }
        }
    }

    // ── Validation ──

    /// <summary>Tests that encoding with zero width throws.</summary>
    [Test]
    public void Encode_ThrowsOnZeroWidth()
    {
        byte[] pixels = new byte[4];
        Assert.Throws<ImageEncodeException>(() => ImageEncodeUtility.EncodePng(pixels, 0, 1));
    }

    /// <summary>Tests that encoding with a negative dimension throws.</summary>
    [Test]
    public void Encode_ThrowsOnNegativeDimension()
    {
        byte[] pixels = new byte[4];
        Assert.Throws<ImageEncodeException>(() => ImageEncodeUtility.EncodePng(pixels, -1, 1));
    }

    /// <summary>Tests that encoding with insufficient pixel data throws.</summary>
    [Test]
    public void Encode_ThrowsOnInsufficientData()
    {
        byte[] pixels = new byte[8]; // need 16 bytes for 2x2 RGBA8
        Assert.Throws<ImageEncodeException>(() => ImageEncodeUtility.EncodePng(pixels, 2, 2));
    }

    // ── CRC32 ──

    /// <summary>Tests that CRC-32 matches a known value.</summary>
    [Test]
    public void Crc32_KnownValue()
    {
        // CRC32 of "IEND" chunk type (empty data) = 0xAE426082
        byte[] iend = "IEND"u8.ToArray();
        uint crc = PngCrc32.Compute(iend);
        Assert.That(crc, Is.EqualTo(0xAE426082u));
    }

    // ── Compressed output size ──

    /// <summary>Tests that solid-color data compresses below its raw size.</summary>
    [Test]
    public void Encode_CompressesData()
    {
        // 32x32 solid color image should compress well
        int w = 32, h = 32;
        byte[] pixels = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            pixels[i * 4] = 128;
            pixels[i * 4 + 1] = 64;
            pixels[i * 4 + 2] = 32;
            pixels[i * 4 + 3] = 255;
        }

        byte[] png = ImageEncodeUtility.EncodePng(pixels, w, h);

        // Raw pixel data = 32*32*4 = 4096 bytes.
        // PNG should be significantly smaller than raw + PNG overhead.
        Assert.That(png.Length, Is.LessThan(w * h * 4), "PNG should compress solid-color image.");
    }

    /// <summary>Tests padded, multi-chunk output on a stream that only supports writing.</summary>
    [TestCase(PngEncodeMode.Balanced)]
    [TestCase(PngEncodeMode.Fast)]
    public void WritePng_PaddedRowsToNonSeekableStream(PngEncodeMode mode)
    {
        const int width = 259, height = 263, rowBytes = width * 4, stride = rowBytes + 7;
        byte[] source = new byte[(height - 1) * stride + rowBytes];
        new Random(23).NextBytes(source);
        source[3] = 0;
        source[stride + 3] = 128;
        byte[] expected = new byte[rowBytes * height];
        for (int y = 0; y < height; y++)
        {
            source.AsSpan(y * stride, rowBytes).CopyTo(expected.AsSpan(y * rowBytes, rowBytes));
        }

        using WriteOnlyStream destination = new();
        ImageEncodeUtility.WritePng(source, width, height, destination, mode, stride);

        Assert.That(destination.CanWrite, Is.True);
        byte[] png = destination.ToArray();
        Assert.That(ValidateChunks(png), Is.GreaterThan(1));
        AssertPixels(png, width, height, expected);
        Assert.That(ImageEncodeUtility.EncodePng(source, width, height, mode, stride).AsSpan().SequenceEqual(png), Is.True);
    }

    /// <summary>Tests that writing starts at the stream's current position and leaves other bytes alone.</summary>
    [Test]
    public void WritePng_StartsAtCurrentPositionWithoutTruncating()
    {
        byte[] source = [45, 63, 91, 0];
        byte[] buffer = new byte[256];
        Array.Fill(buffer, (byte)0xAB);
        using MemoryStream destination = new(buffer);
        destination.Position = 17;

        ImageEncodeUtility.WritePng(source, 1, 1, destination);

        Assert.That(destination.CanWrite, Is.True);
        Assert.That(destination.Length, Is.EqualTo(buffer.Length));
        int end = (int)destination.Position;
        Assert.That(buffer.AsSpan(0, 17).IndexOfAnyExcept((byte)0xAB), Is.EqualTo(-1));
        Assert.That(buffer.AsSpan(end).IndexOfAnyExcept((byte)0xAB), Is.EqualTo(-1));
        AssertPixels(buffer.AsSpan(17, end - 17), 1, 1, source);
    }

    /// <summary>Tests that invalid layout is rejected before writing any bytes.</summary>
    [TestCase(-1)]
    [TestCase(7)]
    [TestCase(int.MaxValue)]
    public void WritePng_InvalidStrideOrDataLength(int stride)
    {
        using MemoryStream destination = new();
        Assert.Throws<ImageEncodeException>(() => ImageEncodeUtility.WritePng(new byte[16], 2, 2, destination, rowStrideBytes: stride));
        Assert.That(destination.Length, Is.Zero);
    }

    /// <summary>Tests that impossible dimensions and null source pointers are rejected.</summary>
    [Test]
    public void EncodePng_RejectsInvalidPointerAndOverflow()
    {
        Assert.Throws<ImageEncodeException>(() => ImageEncodeUtility.EncodePng((byte*)null, 1, 1));
        Assert.Throws<ImageEncodeException>(() => ImageEncodeUtility.EncodePng(Array.Empty<byte>(), int.MaxValue, int.MaxValue));
    }

    /// <summary>Tests that the filter heuristic treats wrapped negative residuals as small differences.</summary>
    [TestCase(PngEncodeMode.Balanced)]
    [TestCase(PngEncodeMode.Fast)]
    public void FilterRow_ScoresSignedResiduals(PngEncodeMode mode)
    {
        byte[] source = [100, 100, 100, 100, 99, 99, 99, 99];
        byte[] scanline = new byte[source.Length + 1];
        PngFilter.FilterRow(source, default, scanline, mode);
        Assert.That(scanline, Is.EqualTo(new byte[] { 1, 100, 100, 100, 100, 255, 255, 255, 255 }));
    }

    /// <summary>Tests filter selection and output against a scalar reference across vector boundaries.</summary>
    [TestCase(PngEncodeMode.Balanced)]
    [TestCase(PngEncodeMode.Fast)]
    public void FilterRow_MatchesReference(PngEncodeMode mode)
    {
        Random random = new(12);
        int lastFilter = mode == PngEncodeMode.Fast ? 2 : 4;
        foreach (int width in new[] { 1, 3, 4, 7, 8, 9, 16, 17, 65, 259 })
        {
            byte[] row = new byte[width * 4];
            byte[] previous = new byte[row.Length];
            byte[] scanline = new byte[row.Length + 1];
            for (int sourceFilter = 0; sourceFilter <= lastFilter; sourceFilter++)
            {
                random.NextBytes(previous);
                for (int i = 0; i < row.Length; i++)
                {
                    int prediction = ReferencePrediction(row, previous, i, sourceFilter);
                    row[i] = unchecked((byte)(prediction + random.Next(-128, 8)));
                }

                AssertReferenceRow(row, previous, scanline, mode, lastFilter);
                AssertReferenceRow(row, default, scanline, mode, lastFilter);
            }
        }
    }

    private static void AssertReferenceRow(
        ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, Span<byte> scanline, PngEncodeMode mode, int lastFilter)
    {
        int bestScore = int.MaxValue;
        byte[] expected = new byte[scanline.Length];
        byte[] candidate = new byte[scanline.Length];
        for (int filter = 0; filter <= lastFilter; filter++)
        {
            candidate[0] = (byte)filter;
            int score = 0;
            for (int i = 0; i < row.Length; i++)
            {
                byte residual = unchecked((byte)(row[i] - ReferencePrediction(row, previous, i, filter)));
                candidate[i + 1] = residual;
                score += Math.Min(residual, 256 - residual);
            }

            if (score < bestScore)
            {
                bestScore = score;
                (expected, candidate) = (candidate, expected);
            }
        }

        PngFilter.FilterRow(row, previous, scanline, mode);
        Assert.That(scanline.SequenceEqual(expected), Is.True, $"Row length {row.Length}, filter {expected[0]}");
    }

    private static int ReferencePrediction(ReadOnlySpan<byte> row, ReadOnlySpan<byte> previous, int i, int filter)
    {
        int a = i >= 4 ? row[i - 4] : 0;
        int b = previous.IsEmpty ? 0 : previous[i];
        int c = i >= 4 && !previous.IsEmpty ? previous[i - 4] : 0;
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return filter switch
        {
            0 => 0,
            1 => a,
            2 => b,
            3 => (a + b) / 2,
            _ => pa <= pb && pa <= pc ? a : pb <= pc ? b : c,
        };
    }

    /// <summary>Tests that exporting a GPU texture preserves all RGBA channels and the caller's stream.</summary>
    [Test]
    [Category("AlcoGpu")]
    [NonParallelizable]
    public void EncodeTextureToPng_PreservesReadbackPixels()
    {
        const int width = 37, height = 19;
        byte[] pixels = new byte[width * height * 4];
        new Random(34).NextBytes(pixels);
        pixels[3] = 0;
        using RenderingTestHost host = Utility.CreateRenderingSystem(GraphicsBackend.Auto);
        Texture texture = TextureUtility.CreateTexture(
            host.RenderingSystem,
            pixels,
            width,
            height,
            Format.Rgba8Unorm,
            TextureUsage.Sampled | TextureUsage.TransferSource | TextureUsage.TransferDestination);
        using WriteOnlyStream destination = new();
        try
        {
            TextureUtility.EncodeTextureToPNG(host.RenderingSystem, texture, destination);

            Assert.That(destination.CanWrite, Is.True);
            AssertPixels(destination.ToArray(), width, height, pixels);
        }
        finally
        {
            if (!texture.IsDestroyed)
            {
                texture.Destroy();
            }
        }
    }

    private static int ValidateChunks(ReadOnlySpan<byte> png)
    {
        const int overhead = 12;
        Assert.That(png[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), Is.True);
        int offset = 8;
        int idatCount = 0;
        bool ended = false;
        while (offset < png.Length)
        {
            ReadOnlySpan<byte> chunk = png[offset..];
            Assert.That(chunk.Length, Is.GreaterThanOrEqualTo(overhead));
            int size = BinaryPrimitives.ReadInt32BigEndian(chunk);
            Assert.That(size, Is.InRange(0, chunk.Length - overhead));
            ReadOnlySpan<byte> type = chunk.Slice(4, 4);
            uint crc = BinaryPrimitives.ReadUInt32BigEndian(chunk.Slice(8 + size, 4));
            Assert.That(Crc32.HashToUInt32(chunk.Slice(4, 4 + size)), Is.EqualTo(crc), $"CRC at {offset}");

            if (offset == 8)
            {
                Assert.That(type.SequenceEqual("IHDR"u8), Is.True);
                Assert.That(size, Is.EqualTo(13));
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                Assert.That(size, Is.Zero);
                Assert.That(offset + overhead, Is.EqualTo(png.Length));
                ended = true;
            }
            else
            {
                Assert.That(type.SequenceEqual("IDAT"u8), Is.True);
                idatCount++;
            }

            offset += size + overhead;
        }

        Assert.That(ended, Is.True);
        return idatCount;
    }

    private static void AssertPixels(ReadOnlySpan<byte> png, int width, int height, ReadOnlySpan<byte> expected)
    {
        byte* decoded = ImageDecodeUtility.DecodePng(png, out int decodedWidth, out int decodedHeight);
        try
        {
            Assert.That(decodedWidth, Is.EqualTo(width));
            Assert.That(decodedHeight, Is.EqualTo(height));
            Assert.That(new ReadOnlySpan<byte>(decoded, expected.Length).SequenceEqual(expected), Is.True);
        }
        finally
        {
            NativeMemory.Free(decoded);
        }
    }

    private sealed class WriteOnlyStream : Stream
    {
        private readonly MemoryStream _buffer = new();

        public override bool CanSeek => false;
        public override bool CanRead => false;
        public override bool CanWrite => _buffer.CanWrite;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Length => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _buffer.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => _buffer.Write(buffer);
        public byte[] ToArray() => _buffer.ToArray();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _buffer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
