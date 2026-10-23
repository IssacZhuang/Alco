using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

/// <summary>Covers image header probing and decoding for PNG, JPEG, and DDS data.</summary>
[TestFixture]
public unsafe class TestImageDecodeUtility
{
    private static byte[] LoadTestFile(string subfolder, string filename)
        => File.ReadAllBytes(Path.Combine("Files", "Image", subfolder, filename));

    /// <summary>Tests that PNG image info reports the correct dimensions.</summary>
    [Test]
    public void GetImageInfo_PNG_ReturnsCorrectDimensions()
    {
        byte[] data = LoadTestFile("Png", "basn0g08.png");
        var info = ImageDecodeUtility.GetImageInfo(data);
        Assert.That(info.Width, Is.EqualTo(32));
        Assert.That(info.Height, Is.EqualTo(32));
    }

    /// <summary>Tests that JPEG image info reports positive dimensions.</summary>
    [Test]
    public void GetImageInfo_JPEG_ReturnsCorrectDimensions()
    {
        byte[] data = LoadTestFile("Jpeg", "test.jpg");
        var info = ImageDecodeUtility.GetImageInfo(data);
        Assert.That(info.Width, Is.GreaterThan(0));
        Assert.That(info.Height, Is.GreaterThan(0));
    }

    /// <summary>Tests that an unrecognized image format throws <see cref="ImageDecodeException"/>.</summary>
    [Test]
    public void GetImageInfo_UnknownFormat_ThrowsException()
    {
        byte[] data = new byte[100];
        Assert.Throws<ImageDecodeException>(() => ImageDecodeUtility.GetImageInfo(data));
    }

    /// <summary>Tests that image data shorter than a header throws <see cref="ImageDecodeException"/>.</summary>
    [Test]
    public void GetImageInfo_TooShort_ThrowsException()
    {
        byte[] data = [0x42];
        Assert.Throws<ImageDecodeException>(() => ImageDecodeUtility.GetImageInfo(data));
    }

    /// <summary>Tests that automatic decoding recognizes and decodes PNG data.</summary>
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

    /// <summary>Tests that automatic decoding recognizes and decodes JPEG data.</summary>
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

    /// <summary>Tests that automatic decoding rejects an unrecognized format.</summary>
    [Test]
    public void DecodeAuto_UnknownFormat_ThrowsException()
    {
        byte[] data = new byte[100];
        Assert.Throws<ImageDecodeException>(() =>
            ImageDecodeUtility.DecodeAuto(data, out _, out _));
    }

    /// <summary>Tests that a decoded PNG buffers exactly width times height RGBA pixels.</summary>
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

    // DDS fourCC codes as little-endian uints.
    private const uint FourCcDxt1 = 0x31545844; // "DXT1"
    private const uint FourCcDx10 = 0x30315844; // "DX10"

    private static byte[] CreateDdsBytes(int width, int height, int mipLevels, uint fourCc, uint dxgiFormat = 0, int payloadBytes = 0)
    {
        int headerSize = fourCc == FourCcDx10 ? 148 : 128;
        byte[] data = new byte[headerSize + payloadBytes];
        Span<byte> span = data;
        BinaryPrimitives.WriteUInt32LittleEndian(span, 0x20534444);   // "DDS " magic
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 124);     // header size
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], height);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], width);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], mipLevels);
        BinaryPrimitives.WriteUInt32LittleEndian(span[80..], 0x4);    // DDPF_FOURCC
        BinaryPrimitives.WriteUInt32LittleEndian(span[84..], fourCc);
        if (fourCc == FourCcDx10)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[128..], dxgiFormat);
        }
        return data;
    }

    /// <summary>Tests that PNG file info reports dimensions, format, and mip count.</summary>
    [Test]
    public void GetImageFileInfo_PNG_ReturnsFileSpec()
    {
        byte[] data = LoadTestFile("Png", "basn0g08.png");
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        Assert.That(info.Width, Is.EqualTo(32));
        Assert.That(info.Height, Is.EqualTo(32));
        Assert.That(info.IsBlockCompressed, Is.False);
        Assert.That(info.MipLevels, Is.EqualTo(1));
        Assert.That(info.DataOffset, Is.EqualTo(0));
    }

    /// <summary>Tests that JPEG file info reports dimensions and a single uncompressed level.</summary>
    [Test]
    public void GetImageFileInfo_JPEG_ReturnsFileSpec()
    {
        byte[] data = LoadTestFile("Jpeg", "test.jpg");
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        Assert.That(info.Width, Is.GreaterThan(0));
        Assert.That(info.Height, Is.GreaterThan(0));
        Assert.That(info.IsBlockCompressed, Is.False);
        Assert.That(info.MipLevels, Is.EqualTo(1));
    }

    /// <summary>Tests that a truncated PNG header throws <see cref="ImageDecodeException"/>.</summary>
    [Test]
    public void GetImageFileInfo_PNG_TruncatedHeader_ThrowsException()
    {
        byte[] data = LoadTestFile("Png", "basn0g08.png");
        Assert.Throws<ImageDecodeException>(() => ImageDecodeUtility.GetImageFileInfo(data.AsSpan(0, 16)));
    }

    /// <summary>Tests that a DDS header is parsed without any payload bytes.</summary>
    [Test]
    public void GetImageFileInfo_Dds_ParsesHeaderWithoutPayload()
    {
        // 64x64 BC1 with a 7-level chain in the header; only levels down to 4x4 are
        // usable (64, 32, 16, 8, 4), so the probed chain has 5 levels. No payload is
        // present: probing must succeed on the header alone.
        byte[] data = CreateDdsBytes(64, 64, 7, FourCcDxt1);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        Assert.That(info.Width, Is.EqualTo(64));
        Assert.That(info.Height, Is.EqualTo(64));
        Assert.That(info.IsBlockCompressed, Is.True);
        Assert.That(info.Format, Is.EqualTo(PixelFormat.BC1RGBAUnorm));
        Assert.That(info.MipLevels, Is.EqualTo(5));
        Assert.That(info.DataOffset, Is.EqualTo(128));
    }

    /// <summary>Tests that requesting sRGB on a DDS returns the sRGB pixel format variant.</summary>
    [Test]
    public void GetImageFileInfo_Dds_SrgbFlagSelectsSrgbVariant()
    {
        byte[] data = CreateDdsBytes(64, 64, 1, FourCcDxt1);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data, srgb: true);
        Assert.That(info.Format, Is.EqualTo(PixelFormat.BC1RGBAUnormSrgb));
    }

    /// <summary>Tests that a DX10 extended DDS header resolves its DXGI format.</summary>
    [Test]
    public void GetImageFileInfo_DdsDx10_ParsesExtendedHeader()
    {
        // DXGI_FORMAT_BC7_UNORM = 98
        byte[] data = CreateDdsBytes(16, 16, 3, FourCcDx10, dxgiFormat: 98);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(data);
        Assert.That(info.IsBlockCompressed, Is.True);
        Assert.That(info.Format, Is.EqualTo(PixelFormat.BC7RGBAUnorm));
        Assert.That(info.MipLevels, Is.EqualTo(3));
        Assert.That(info.DataOffset, Is.EqualTo(148));
    }

    /// <summary>Tests that an unrecognized file format throws <see cref="ImageDecodeException"/>.</summary>
    [Test]
    public void GetImageFileInfo_UnknownFormat_ThrowsException()
    {
        byte[] data = new byte[256];
        Assert.Throws<ImageDecodeException>(() => ImageDecodeUtility.GetImageFileInfo(data));
    }

    /// <summary>Tests that probing a PNG stream consumes exactly 33 bytes.</summary>
    [Test]
    public void GetImageFileInfo_Stream_PNG_ReadsExactly33Bytes()
    {
        byte[] data = LoadTestFile("Png", "basn0g08.png");
        using var stream = new MemoryStream(data);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(stream);
        Assert.That(info.Width, Is.EqualTo(32));
        Assert.That(info.Height, Is.EqualTo(32));
        Assert.That(info.IsBlockCompressed, Is.False);
        Assert.That(stream.Position, Is.EqualTo(33));
    }

    /// <summary>Tests that probing a JPEG stream reads only up to the SOF segment.</summary>
    [Test]
    public void GetImageFileInfo_Stream_JPEG_ReadsOnlySegmentHeaders()
    {
        byte[] data = LoadTestFile("Jpeg", "test.jpg");
        using var stream = new MemoryStream(data);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(stream);
        Assert.That(info.Width, Is.GreaterThan(0));
        Assert.That(info.Height, Is.GreaterThan(0));
        Assert.That(info.IsBlockCompressed, Is.False);
        // Only the header up to the SOF segment is consumed, not the scan data.
        Assert.That(stream.Position, Is.LessThan(data.Length));
    }

    /// <summary>Tests that probing a DDS stream consumes exactly the 128-byte header.</summary>
    [Test]
    public void GetImageFileInfo_Stream_Dds_ReadsExactly128Bytes()
    {
        byte[] data = CreateDdsBytes(64, 64, 7, FourCcDxt1, payloadBytes: 4096);
        using var stream = new MemoryStream(data);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(stream);
        Assert.That(info.IsBlockCompressed, Is.True);
        Assert.That(info.MipLevels, Is.EqualTo(5));
        Assert.That(stream.Position, Is.EqualTo(128));
    }

    /// <summary>Tests that probing a DX10 DDS stream consumes exactly the 148-byte header.</summary>
    [Test]
    public void GetImageFileInfo_Stream_DdsDx10_ReadsExactly148Bytes()
    {
        byte[] data = CreateDdsBytes(16, 16, 3, FourCcDx10, dxgiFormat: 98, payloadBytes: 4096);
        using var stream = new MemoryStream(data);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(stream);
        Assert.That(info.Format, Is.EqualTo(PixelFormat.BC7RGBAUnorm));
        Assert.That(info.DataOffset, Is.EqualTo(148));
        Assert.That(stream.Position, Is.EqualTo(148));
    }

    /// <summary>Tests that a truncated PNG stream throws <see cref="ImageDecodeException"/>.</summary>
    [Test]
    public void GetImageFileInfo_Stream_Truncated_ThrowsException()
    {
        byte[] data = LoadTestFile("Png", "basn0g08.png");
        using var stream = new MemoryStream(data[..20]);
        Assert.Throws<ImageDecodeException>(() => ImageDecodeUtility.GetImageFileInfo(stream));
    }
}
