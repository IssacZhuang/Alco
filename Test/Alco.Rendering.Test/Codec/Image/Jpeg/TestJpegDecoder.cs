using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

/// <summary>Tests the JPEG decoder against a real-world image.</summary>
[TestFixture]
public unsafe class TestJpegDecoder
{
    /// <summary>Tests that decoding a real-world JPEG produces non-null pixels and positive dimensions.</summary>
    [Test]
    public void Decode_RealWorld_Jpg()
    {
        string path = Path.Combine("Files", "Image", "Jpeg", "test.jpg");
        if (!File.Exists(path))
            Assert.Ignore($"Test file not found: {path}");

        byte[] fileData = File.ReadAllBytes(path);

        byte* pixels = ImageDecodeUtility.DecodeJpeg(fileData, out int w, out int h);

        try
        {
            Assert.That((nint)pixels, Is.Not.EqualTo(0), "Decoded pixels should not be null");
            Assert.That(w, Is.GreaterThan(0), "Width should be positive");
            Assert.That(h, Is.GreaterThan(0), "Height should be positive");
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }
}
