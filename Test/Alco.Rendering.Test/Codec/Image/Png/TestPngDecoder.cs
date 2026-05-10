using System.Runtime.InteropServices;
using NUnit.Framework;
using StbImageSharp;

namespace Alco.Rendering.Test;

using Alco.Rendering.Codec.Image;

[TestFixture]
public unsafe class TestPngDecoder
{
    /// <summary>
    /// Load a PNG file, decode with both the new decoder and STB reference, then compare RGBA8 pixel data.
    /// </summary>
    private static void AssertDecodesCorrectly(string filename)
    {
        string path = Path.Combine("Files", "Image", "Png", filename);
        byte[] fileData = File.ReadAllBytes(path);

        // Decode with new decoder
        byte* newPixels;
        int newW, newH;
        fixed (byte* ptr = fileData)
        {
            newPixels = ImageDecodeUtility.DecodePng(fileData, out newW, out newH);
        }

        try
        {
            // Decode with STB reference
            using ImageResultBuffer stbImage = ImageResultBuffer.FromMemory(fileData, ColorComponents.RedGreenBlueAlpha);

            // Compare dimensions
            Assert.That(newW, Is.EqualTo(stbImage.Width), "Width mismatch");
            Assert.That(newH, Is.EqualTo(stbImage.Height), "Height mismatch");

            // Compare pixel data
            int pixelCount = newW * newH * 4;
            byte* stbPixels = stbImage.UnsafePointer;
            for (int i = 0; i < pixelCount; i++)
            {
                if (newPixels[i] != stbPixels[i])
                {
                    int pixel = i / 4;
                    int channel = i % 4;
                    Assert.Fail($"Pixel mismatch at ({pixel % newW}, {pixel / newW}) channel {channel}: " +
                        $"new={newPixels[i]}, stb={stbPixels[i]}");
                }
            }
        }
        finally
        {
            NativeMemory.Free(newPixels);
        }
    }

    // Grayscale tests
    [Test] public void Decode_Grayscale1Bit() => AssertDecodesCorrectly("basn0g01.png");
    [Test] public void Decode_Grayscale1Bit_Interlaced() => AssertDecodesCorrectly("basi0g01.png");
    [Test] public void Decode_Grayscale2Bit() => AssertDecodesCorrectly("basn0g02.png");
    [Test] public void Decode_Grayscale4Bit() => AssertDecodesCorrectly("basn0g04.png");
    [Test] public void Decode_Grayscale8Bit() => AssertDecodesCorrectly("basn0g08.png");
    [Test] public void Decode_Grayscale16Bit() => AssertDecodesCorrectly("basn0g16.png");
    [Test] public void Decode_Grayscale16Bit_Interlaced() => AssertDecodesCorrectly("basi0g16.png");

    // RGB tests
    [Test] public void Decode_RGB8Bit() => AssertDecodesCorrectly("basn2c08.png");
    [Test] public void Decode_RGB16Bit() => AssertDecodesCorrectly("basn2c16.png");
    [Test] public void Decode_RGB8Bit_Interlaced() => AssertDecodesCorrectly("basi2c08.png");

    // Indexed tests
    [Test] public void Decode_Indexed1Bit() => AssertDecodesCorrectly("basn3p01.png");
    [Test] public void Decode_Indexed2Bit() => AssertDecodesCorrectly("basn3p02.png");
    [Test] public void Decode_Indexed4Bit() => AssertDecodesCorrectly("basn3p04.png");
    [Test] public void Decode_Indexed8Bit() => AssertDecodesCorrectly("basn3p08.png");
    [Test] public void Decode_Indexed8Bit_Interlaced() => AssertDecodesCorrectly("basi3p08.png");

    // Grayscale + Alpha tests
    [Test] public void Decode_GrayAlpha8Bit() => AssertDecodesCorrectly("basn4a08.png");
    [Test] public void Decode_GrayAlpha16Bit() => AssertDecodesCorrectly("basn4a16.png");

    // RGBA tests
    [Test] public void Decode_RGBA8Bit() => AssertDecodesCorrectly("basn6a08.png");
    [Test] public void Decode_RGBA16Bit() => AssertDecodesCorrectly("basn6a16.png");
    [Test] public void Decode_RGBA8Bit_Interlaced() => AssertDecodesCorrectly("basi6a08.png");

    // Compression variants
    [Test] public void Decode_CompressionZ00() => AssertDecodesCorrectly("z00n2c08.png");
    [Test] public void Decode_CompressionZ09() => AssertDecodesCorrectly("z09n2c08.png");

    // Corrupt files - must throw ImageDecodeException, not crash
    [Test] public void Decode_Corrupt_Xc1n0g08() => AssertThrowsOnCorrupt("xc1n0g08.png");
    [Test] public void Decode_Corrupt_Xc9n2c08() => AssertThrowsOnCorrupt("xc9n2c08.png");
    [Test] public void Decode_Corrupt_Xcrn0g04() => AssertThrowsOnCorrupt("xcrn0g04.png");
    [Test] public void Decode_Corrupt_Xd0n2c08() => AssertThrowsOnCorrupt("xd0n2c08.png");
    [Test] public void Decode_Corrupt_Xdtn0g01() => AssertThrowsOnCorrupt("xdtn0g01.png");

    /// <summary>
    /// Verify that decoding a corrupt PNG file throws <see cref="ImageDecodeException"/>.
    /// </summary>
    private static void AssertThrowsOnCorrupt(string filename)
    {
        string path = Path.Combine("Files", "Image", "Png", filename);
        byte[] fileData = File.ReadAllBytes(path);
        Assert.Throws<ImageDecodeException>(() =>
        {
            byte* result = ImageDecodeUtility.DecodePng(fileData, out int w, out int h);
            if (result != null)
                NativeMemory.Free(result);
        });
    }
}
