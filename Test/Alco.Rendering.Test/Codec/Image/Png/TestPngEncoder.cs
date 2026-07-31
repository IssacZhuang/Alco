using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

[TestFixture]
public unsafe class TestPngEncoder
{
    // ── Round-trip: encode RGBA8 → decode PNG → verify pixel-perfect ──

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

    [Test]
    public void Encode_ThrowsOnZeroWidth()
    {
        byte[] pixels = new byte[4];
        Assert.Throws<ImageEncodeException>(() => ImageEncodeUtility.EncodePng(pixels, 0, 1));
    }

    [Test]
    public void Encode_ThrowsOnNegativeDimension()
    {
        byte[] pixels = new byte[4];
        Assert.Throws<ImageEncodeException>(() => ImageEncodeUtility.EncodePng(pixels, -1, 1));
    }

    [Test]
    public void Encode_ThrowsOnInsufficientData()
    {
        byte[] pixels = new byte[8]; // need 16 bytes for 2x2 RGBA8
        Assert.Throws<ImageEncodeException>(() => ImageEncodeUtility.EncodePng(pixels, 2, 2));
    }

    // ── CRC32 ──

    [Test]
    public void Crc32_KnownValue()
    {
        // CRC32 of "IEND" chunk type (empty data) = 0xAE426082
        byte[] iend = "IEND"u8.ToArray();
        uint crc = PngCrc32.Compute(iend);
        Assert.That(crc, Is.EqualTo(0xAE426082u));
    }

    // ── Compressed output size ──

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
}
