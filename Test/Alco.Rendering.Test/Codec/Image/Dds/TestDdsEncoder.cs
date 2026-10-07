using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

/// <summary>
/// DDS file assembly: encoded files round-trip through DdsDecoder and the header
/// probes, and invalid families, dimensions and mip chains are rejected.
/// </summary>
[TestFixture]
public class TestDdsEncoder
{
    [Test]
    public void Encode_Bc3_SpecRoundTripsThroughDecoder()
    {
        // 64x64 BC3, 4 levels: 4096 + 1024 + 256 + 64 block bytes.
        byte[] chain = new byte[5440];
        byte[] file = DdsEncoder.Encode(64, 64, DdsDecoder.BcFamily.BC3, chain, mipLevels: 4);

        DdsDecoder.ParseHeader(file, srgb: false, out DdsDecoder.BcFamily family, out PixelFormat format,
            out int width, out int height, out int mipLevels, out int dataOffset);

        Assert.That(family, Is.EqualTo(DdsDecoder.BcFamily.BC3));
        Assert.That(format, Is.EqualTo(PixelFormat.BC3RGBAUnorm));
        Assert.That(width, Is.EqualTo(64));
        Assert.That(height, Is.EqualTo(64));
        Assert.That(mipLevels, Is.EqualTo(4));
        Assert.That(dataOffset, Is.EqualTo(128));
        Assert.That(file.Length, Is.EqualTo(128 + 5440));
    }

    [Test]
    public void Encode_Bc1_MapsToDxt1AndSrgbVariant()
    {
        // 8x8 BC1, 1 level: 2x2 blocks of 8 bytes.
        byte[] chain = new byte[32];
        byte[] file = DdsEncoder.Encode(8, 8, DdsDecoder.BcFamily.BC1, chain, mipLevels: 1);

        DdsDecoder.ParseHeader(file, srgb: true, out _, out PixelFormat format,
            out int width, out int height, out int mipLevels, out int dataOffset);

        Assert.That(format, Is.EqualTo(PixelFormat.BC1RGBAUnormSrgb));
        Assert.That(width, Is.EqualTo(8));
        Assert.That(height, Is.EqualTo(8));
        Assert.That(mipLevels, Is.EqualTo(1));
        Assert.That(dataOffset, Is.EqualTo(128));
    }

    [Test]
    public void Encode_ProducesProbeableHeader()
    {
        // 16x16 BC3 with 2 levels: 256 + 64 block bytes.
        byte[] file = DdsEncoder.Encode(16, 16, DdsDecoder.BcFamily.BC3, new byte[320], mipLevels: 2);

        // The stream probe (used by the streaming loader) accepts the encoded bytes.
        using MemoryStream stream = new(file);
        ImageFileInfo info = ImageDecodeUtility.GetImageFileInfo(stream, srgb: false);

        Assert.That(info.IsBlockCompressed, Is.True);
        Assert.That(info.Format, Is.EqualTo(PixelFormat.BC3RGBAUnorm));
        Assert.That(info.Width, Is.EqualTo(16));
        Assert.That(info.Height, Is.EqualTo(16));
        Assert.That(info.MipLevels, Is.EqualTo(2));
        Assert.That(info.DataOffset, Is.EqualTo(128));

        // The span probe agrees.
        ImageFileInfo spanInfo = ImageDecodeUtility.GetImageFileInfo(file.AsSpan(0, 128), srgb: false);
        Assert.That(spanInfo.MipLevels, Is.EqualTo(2));
    }

    [Test]
    public void Encode_SubBlockDimensions_Throw()
    {
        Assert.Throws<ImageDecodeException>(
            () => DdsEncoder.Encode(6, 8, DdsDecoder.BcFamily.BC1, new byte[8], mipLevels: 1));
        Assert.Throws<ImageDecodeException>(
            () => DdsEncoder.Encode(0, 8, DdsDecoder.BcFamily.BC1, new byte[8], mipLevels: 1));
    }

    [Test]
    public void Encode_UnalignedMipLevel_Throws()
    {
        // An 8x8 image supports 2 aligned levels (8x8 and 4x4); a third would be 2x2.
        Assert.Throws<ImageDecodeException>(
            () => DdsEncoder.Encode(8, 8, DdsDecoder.BcFamily.BC1, new byte[40], mipLevels: 3));
    }

    [Test]
    public void Encode_WrongChainLength_Throws()
    {
        // 16x16 BC1 with 2 levels needs 32 + 8 bytes.
        Assert.Throws<ImageDecodeException>(
            () => DdsEncoder.Encode(16, 16, DdsDecoder.BcFamily.BC1, new byte[32], mipLevels: 2));
    }

    [Test]
    public void Encode_UnsupportedFamily_Throws()
    {
        Assert.Throws<ImageDecodeException>(
            () => DdsEncoder.Encode(8, 8, DdsDecoder.BcFamily.BC7, new byte[32], mipLevels: 1));
    }
}
