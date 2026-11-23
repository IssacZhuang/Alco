using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using Alco.Graphics;
using Alco.Rendering;
using Alco.TextureConverter;
using Converter = Alco.TextureConverter.TextureConverter;
using NUnit.Framework;

namespace Alco.Engine.Test;

/// <summary>End-to-end integration tests for <see cref="Converter"/>.</summary>
[TestFixture]
[NonParallelizable]
public class TestTextureConverter
{
    private string _tempDir = "";

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "alco_texconv_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public void TryConvert_Bc7SrgbWithMipsVerify_And_PremultiplyMeta_ProducesValidDx10Dds()
    {
        string subDir = Path.Combine(_tempDir, "Item");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, ".texture-option.meta"), """{ "PremultiplyAlpha": true }""");

        // 16x16 gradient with half-transparent and zero-alpha pixels to test PremultiplyAlpha + mips.
        byte[] rgba = new byte[16 * 16 * 4];
        for (int y = 0; y < 16; ++y)
        {
            for (int x = 0; x < 16; ++x)
            {
                int i = (y * 16 + x) * 4;
                rgba[i + 0] = (byte)(40 + x * 12);
                rgba[i + 1] = (byte)(60 + y * 10);
                rgba[i + 2] = 200;
                rgba[i + 3] = y < 4 ? (byte)0 : y < 8 ? (byte)128 : (byte)255;
            }
        }

        string pngPath = Path.Combine(subDir, "item.png");
        WriteRgba8Png(pngPath, 16, 16, rgba);
        WriteRgba8Png(Path.Combine(_tempDir, "odd.png"), 10, 10, new byte[10 * 10 * 4]);

        using Converter converter = new();
        bool ran = converter.TryConvert(
            new TextureConverterOptions(
                _tempDir,
                Format: TextureConverterFormat.Bc7,
                Verify: true,
                Srgb: true),
            out TextureConverterResult? result,
            out string? error);

        Assert.That(ran, Is.True, error);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Converted, Is.EqualTo(1));
        Assert.That(result.Skipped, Is.EqualTo(1));
        Assert.That(result.Failed, Is.EqualTo(0));

        TextureConverterFileReport itemReport = result.Files.Single(f => f.DisplayName.EndsWith("item.png", StringComparison.Ordinal));
        Assert.That(itemReport.Levels, Is.EqualTo(3)); // 16x16 -> 8x8 -> 4x4
        Assert.That(itemReport.PsnrDecibel, Is.GreaterThan(30.0));

        byte[] ddsBytes = File.ReadAllBytes(Path.ChangeExtension(pngPath, ".dds"));
        Assert.That(DdsDecoder.HasDx10Header(ddsBytes), Is.True);
        DdsDecoder.Decode(ddsBytes, srgb: false, out DdsDecoder.BcFamily family, out Format decodedFormat,
            out int width, out int height, out int mipLevels, out int dataOffset);

        Assert.That(family, Is.EqualTo(DdsDecoder.BcFamily.BC7));
        Assert.That(decodedFormat, Is.EqualTo(Format.Bc7Srgb));
        Assert.That(width, Is.EqualTo(16));
        Assert.That(height, Is.EqualTo(16));
        Assert.That(mipLevels, Is.EqualTo(3));
        Assert.That(dataOffset, Is.EqualTo(148));
    }

    private static void WriteRgba8Png(string path, int width, int height, ReadOnlySpan<byte> rgba)
    {
        using FileStream fs = File.Create(path);
        fs.Write([137, 80, 78, 71, 13, 10, 26, 10]); // PNG signature

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(fs, "IHDR"u8, ihdr);

        using MemoryStream idatStream = new();
        using (ZLibStream zlib = new(idatStream, CompressionLevel.NoCompression, leaveOpen: true))
        {
            byte[] scanline = new byte[1 + width * 4];
            for (int y = 0; y < height; ++y)
            {
                scanline[0] = 0; // None filter
                rgba.Slice(y * width * 4, width * 4).CopyTo(scanline.AsSpan(1));
                zlib.Write(scanline);
            }
        }
        WriteChunk(fs, "IDAT"u8, idatStream.ToArray());
        WriteChunk(fs, "IEND"u8, []);
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        stream.Write(len);
        stream.Write(type);
        stream.Write(data);

        Crc32 crc = new();
        crc.Append(type);
        crc.Append(data);
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc.GetCurrentHashAsUInt32());
        stream.Write(crcBytes);
    }
}
