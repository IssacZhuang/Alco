using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

[TestFixture]
public unsafe class TestImageDecodeUtility
{
    private static byte[] LoadTestFile(string subfolder, string filename)
        => File.ReadAllBytes(Path.Combine("Files", "Image", subfolder, filename));

    [Test]
    public void GetImageInfo_PNG_ReturnsCorrectDimensions()
    {
        byte[] data = LoadTestFile("Png", "basn0g08.png");
        var info = ImageDecodeUtility.GetImageInfo(data);
        Assert.That(info.Width, Is.EqualTo(32));
        Assert.That(info.Height, Is.EqualTo(32));
    }

    [Test]
    public void GetImageInfo_JPEG_ReturnsCorrectDimensions()
    {
        byte[] data = LoadTestFile("Jpeg", "test.jpg");
        var info = ImageDecodeUtility.GetImageInfo(data);
        Assert.That(info.Width, Is.GreaterThan(0));
        Assert.That(info.Height, Is.GreaterThan(0));
    }

    [Test]
    public void GetImageInfo_UnknownFormat_ThrowsException()
    {
        byte[] data = new byte[100];
        Assert.Throws<ImageDecodeException>(() => ImageDecodeUtility.GetImageInfo(data));
    }

    [Test]
    public void GetImageInfo_TooShort_ThrowsException()
    {
        byte[] data = [0x42];
        Assert.Throws<ImageDecodeException>(() => ImageDecodeUtility.GetImageInfo(data));
    }

    [Test]
    public void DecodeAuto_PNG_DecodesCorrectly()
    {
        byte[] data = LoadTestFile("Png", "basn6a08.png");
        byte* pixels = ImageDecodeUtility.DecodeAuto(data, out int w, out int h);
        try
        {
            Assert.That(w, Is.EqualTo(32));
            Assert.That(h, Is.EqualTo(32));
        }
        finally { NativeMemory.Free(pixels); }
    }

    [Test]
    public void DecodeAuto_JPEG_DecodesCorrectly()
    {
        byte[] data = LoadTestFile("Jpeg", "test.jpg");
        byte* pixels = ImageDecodeUtility.DecodeAuto(data, out int w, out int h);
        try
        {
            Assert.That(w, Is.GreaterThan(0));
            Assert.That(h, Is.GreaterThan(0));
        }
        finally { NativeMemory.Free(pixels); }
    }

    [Test]
    public void DecodeAuto_UnknownFormat_ThrowsException()
    {
        byte[] data = new byte[100];
        Assert.Throws<ImageDecodeException>(() =>
            ImageDecodeUtility.DecodeAuto(data, out _, out _));
    }

    [Test]
    public void Decode_ResultSize()
    {
        byte[] data = LoadTestFile("Png", "basn0g08.png");
        byte* pixels = ImageDecodeUtility.DecodePng(data, out int w, out int h);
        try
        {
            // Verify the output pointer is valid by reading all pixels
            int totalBytes = w * h * 4;
            for (int i = 0; i < totalBytes; i++)
                _ = pixels[i]; // access each byte to verify readable
            Assert.That(w * h, Is.EqualTo(32 * 32));
        }
        finally { NativeMemory.Free(pixels); }
    }
}
