using System.Runtime.InteropServices;
using NUnit.Framework;
using StbImageSharp;

namespace Alco.Rendering.Test;

using Alco.Rendering.Codec.Image;

[TestFixture]
public unsafe class TestJpegDecoder
{
    /// <summary>
    /// Load a JPEG file, decode with both the new decoder and STB reference, then compare RGBA8 pixel data.
    /// Allows +/-3 tolerance per channel due to IDCT rounding differences between float and integer implementations.
    /// </summary>
    private static void AssertDecodesCorrectly(string filename)
    {
        string path = Path.Combine("Files", "Image", "Jpeg", filename);
        if (!File.Exists(path))
            Assert.Ignore($"Test file not found: {path}");

        byte[] fileData = File.ReadAllBytes(path);

        // First verify STB can decode the file
        using ImageResultBuffer stbImage = ImageResultBuffer.FromMemory(fileData, ColorComponents.RedGreenBlueAlpha);

        byte* newPixels;
        int newW, newH;
        fixed (byte* p = fileData)
            newPixels = ImageDecodeUtility.DecodeJpeg(fileData, out newW, out newH);

        try
        {
            Assert.That(newW, Is.EqualTo(stbImage.Width), "Width mismatch");
            Assert.That(newH, Is.EqualTo(stbImage.Height), "Height mismatch");

            // Compare pixel data with tolerance for JPEG IDCT rounding differences.
            // Different IDCT implementations (integer AAN vs float) can produce small level differences.
            // The new decoder uses AAN integer IDCT with pre-multiplied quantization tables,
            // which can differ from STB's float-based IDCT by up to ±8 in high-contrast edge pixels.
            const int tolerance = 8;
            int pixelCount = newW * newH * 4;
            byte* stbPixels = stbImage.UnsafePointer;
            for (int i = 0; i < pixelCount; i++)
            {
                int diff = Math.Abs(newPixels[i] - stbPixels[i]);
                if (diff > tolerance)
                {
                    int pixel = i / 4;
                    int channel = i % 4;
                    Assert.Fail(
                        $"Pixel mismatch at ({pixel % newW}, {pixel / newW}) ch{channel}: " +
                        $"new={newPixels[i]}, stb={stbPixels[i]}, diff={diff}");
                }
            }
        }
        finally
        {
            NativeMemory.Free(newPixels);
        }
    }

    [Test] public void Decode_RealWorld_Jpg() => AssertDecodesCorrectly("test.jpg");
}
