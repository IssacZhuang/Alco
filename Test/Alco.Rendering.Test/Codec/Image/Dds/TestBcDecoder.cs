using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Alco.Graphics;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

/// <summary>
/// CPU fallback decode of block-compressed payloads: hand-crafted blocks cover the
/// BC1 color modes, BC2/BC3 alpha, BC5 UNORM channels, and unsupported formats.
/// </summary>
[TestFixture]
public unsafe class TestBcDecoder
{
    /// <summary>Decode a payload of one 4x4 block and return its 16 RGBA pixels.</summary>
    private static byte[] DecodeSingleBlock(byte[] block, DdsDecoder.BcFamily family)
    {
        byte* pixels = BcDecoder.DecodeLevel(block, 0, family, 4, 4, 0);
        try
        {
            byte[] result = new byte[4 * 4 * 4];
            new Span<byte>(pixels, result.Length).CopyTo(result.AsSpan());
            return result;
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }

    private static byte[] Bc1ColorBlock(ushort color0, ushort color1, uint indices)
    {
        byte[] block = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(block, color0);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(2), color1);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(4), indices);
        return block;
    }

    private static byte[] Bc3Block(byte a0, byte a1, ulong alphaIndices, ushort color0, ushort color1, uint colorIndices)
    {
        byte[] block = new byte[16];
        block[0] = a0;
        block[1] = a1;
        BinaryPrimitives.WriteUInt64LittleEndian(block.AsSpan(2), alphaIndices);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(8), color0);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(10), color1);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(12), colorIndices);
        return block;
    }

    /// <summary>Verifies a solid BC1 block with equal endpoints.</summary>
    [Test]
    public void Bc1_SolidBlock_DecodesToReplicatedColor()
    {
        // Equal endpoints select the 3-color mode; every index points at color 0.
        ushort white = 0xFFFF;
        byte[] pixels = DecodeSingleBlock(Bc1ColorBlock(white, white, 0), DdsDecoder.BcFamily.BC1);

        for (int i = 0; i < 16; i++)
        {
            Assert.That(pixels[(i * 4)..(i * 4 + 4)], Is.EqualTo(new byte[] { 255, 255, 255, 255 }), $"pixel {i}");
        }
    }

    /// <summary>Verifies that the endpoints occupy palette indices zero and one.</summary>
    [Test]
    public void Bc1_FourColorMode_DecodesBothEndpoints()
    {
        byte[] pixels = DecodeSingleBlock(
            Bc1ColorBlock(0xFFFF, 0x0000, 1u << 2),
            DdsDecoder.BcFamily.BC1);

        Assert.That(pixels[0..4], Is.EqualTo(new byte[] { 255, 255, 255, 255 }));
        Assert.That(pixels[4..8], Is.EqualTo(new byte[] { 0, 0, 0, 255 }));
    }

    /// <summary>Verifies transparent black for the BC1 punchthrough palette entry.</summary>
    [Test]
    public void Bc1_ThreeColorMode_Index3IsTransparent()
    {
        // color0 < color1 selects the punchthrough mode: index 3 decodes to
        // transparent black while index 0 keeps color 0 opaque.
        byte[] pixels = DecodeSingleBlock(
            Bc1ColorBlock(0xF800, 0xFFFF, 3u << 6),
            DdsDecoder.BcFamily.BC1);

        Assert.That(pixels[0..4], Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
        Assert.That(pixels[4..8], Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
        Assert.That(pixels[8..12], Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
        Assert.That(pixels[12..16], Is.EqualTo(new byte[] { 0, 0, 0, 0 }));       // pixel 3, index 3 = punchthrough
    }

    /// <summary>Verifies endpoint-first color palettes for every supported BC family.</summary>
    /// <param name="family">The block format to decode.</param>
    [TestCase(DdsDecoder.BcFamily.BC1)]
    [TestCase(DdsDecoder.BcFamily.BC2)]
    [TestCase(DdsDecoder.BcFamily.BC3)]
    public void FourColorPalette_MatchesIntegerMix(DdsDecoder.BcFamily family)
    {
        uint indices = (1u << 2) | (2u << 4) | (3u << 6);
        byte[] block = family == DdsDecoder.BcFamily.BC1
            ? Bc1ColorBlock(0xF800, 0x07E0, indices)
            : Bc3Block(255, 255, 0, 0xF800, 0x07E0, indices);
        if (family == DdsDecoder.BcFamily.BC2)
        {
            Array.Fill(block, (byte)255, 0, 8);
        }
        byte[] pixels = DecodeSingleBlock(block, family);

        Assert.That(pixels[0..4], Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
        Assert.That(pixels[4..8], Is.EqualTo(new byte[] { 0, 255, 0, 255 }));
        Assert.That(pixels[8..12], Is.EqualTo(new byte[] { 170, 85, 0, 255 }));
        Assert.That(pixels[12..16], Is.EqualTo(new byte[] { 85, 170, 0, 255 }));
    }

    /// <summary>Verifies every alpha entry across byte and 32-bit packing boundaries.</summary>
    [Test]
    public void Bc3_AlphaInterpolation_MatchesEightValuePalette()
    {
        ulong alphaIndices = 0;
        for (int i = 0; i < 16; i++)
        {
            alphaIndices |= (ulong)(i % 8) << (i * 3);
        }
        byte[] pixels = DecodeSingleBlock(
            Bc3Block(255, 0, alphaIndices, 0xFFFF, 0xFFFF, 0),
            DdsDecoder.BcFamily.BC3);
        byte[] expected = [255, 0, 218, 182, 145, 109, 72, 36];

        for (int i = 0; i < 16; i++)
        {
            Assert.That(pixels[i * 4 + 3], Is.EqualTo(expected[i % 8]), $"pixel {i}");
        }
    }

    /// <summary>Verifies explicit zero and opaque alpha in the six-value mode.</summary>
    [Test]
    public void Bc3_AlphaSixValueMode_HasExplicitBlackAndWhiteEntries()
    {
        // a0=0 <= a1=255: the 6-value palette appends 0 and 255; index 6 -> 0,
        // index 7 -> 255. Pixel 0 carries index 0 (= a0 = 0).
        ulong alphaIndices = (6UL << 3) | (7UL << 6);
        byte[] pixels = DecodeSingleBlock(
            Bc3Block(0, 255, alphaIndices, 0xFFFF, 0xFFFF, 0),
            DdsDecoder.BcFamily.BC3);

        Assert.That(pixels[3], Is.EqualTo(0));
        Assert.That(pixels[7], Is.EqualTo(0));
        Assert.That(pixels[11], Is.EqualTo(255));
    }

    /// <summary>Verifies four-color interpolation with reversed endpoints and separate alpha.</summary>
    /// <param name="family">The block format to decode.</param>
    [TestCase(DdsDecoder.BcFamily.BC2)]
    [TestCase(DdsDecoder.BcFamily.BC3)]
    public void SeparateAlpha_ReversedEndpoints_StillUsesFourColors(DdsDecoder.BcFamily family)
    {
        uint colorIndices = (1u << 2) | (2u << 4) | (3u << 6);
        byte[] block = Bc3Block(68, 255, 7UL, 0x0000, 0xFFFF, colorIndices);
        if (family == DdsDecoder.BcFamily.BC2)
        {
            Array.Fill(block, (byte)0x44, 0, 8);
            block[0] = 0x4F;
        }
        byte[] pixels = DecodeSingleBlock(block, family);

        Assert.That(pixels[0..4], Is.EqualTo(new byte[] { 0, 0, 0, 255 }));
        Assert.That(pixels[4..8], Is.EqualTo(new byte[] { 255, 255, 255, 68 }));
        Assert.That(pixels[8..12], Is.EqualTo(new byte[] { 85, 85, 85, 68 }));
        Assert.That(pixels[12..16], Is.EqualTo(new byte[] { 170, 170, 170, 68 }));
    }

    /// <summary>Verifies expansion of BC2 four-bit alpha values.</summary>
    [Test]
    public void Bc2_FourBitAlpha_ExpandsNibbles()
    {
        byte[] block = new byte[16];
        block[0] = 0xF0;   // pixel 0 -> 0x0 (0), pixel 1 -> 0xF (255)
        block[1] = 0x77;   // pixels 2,3 -> 0x7 (119)
        ushort white = 0xFFFF;
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(8), white);
        BinaryPrimitives.WriteUInt16LittleEndian(block.AsSpan(10), white);

        byte[] pixels = DecodeSingleBlock(block, DdsDecoder.BcFamily.BC2);

        Assert.That(pixels[3], Is.EqualTo(0));
        Assert.That(pixels[7], Is.EqualTo(255));
        Assert.That(pixels[11], Is.EqualTo(119));
        Assert.That(pixels[15], Is.EqualTo(119));
    }

    /// <summary>Verifies that BC5 independently decodes both interpolation modes into red and green.</summary>
    [Test]
    public void Bc5_Unorm_DecodesIndependentChannels()
    {
        byte[] block = new byte[16];
        block[0] = 255;
        block[8] = 50;
        block[9] = 250;
        ulong redIndices = 0;
        ulong greenIndices = 0;
        for (int i = 0; i < 16; i++)
        {
            redIndices |= (ulong)(i % 8) << (i * 3);
            greenIndices |= (ulong)(7 - i % 8) << (i * 3);
        }
        for (int i = 0; i < 6; i++)
        {
            block[2 + i] = (byte)(redIndices >> (i * 8));
            block[10 + i] = (byte)(greenIndices >> (i * 8));
        }
        byte[] pixels = DecodeSingleBlock(block, DdsDecoder.BcFamily.BC5);
        byte[] red = [255, 0, 218, 182, 145, 109, 72, 36];
        byte[] green = [50, 250, 90, 130, 170, 210, 0, 255];
        for (int i = 0; i < 16; i++)
        {
            Assert.That(pixels[(i * 4)..(i * 4 + 4)], Is.EqualTo(new byte[] { red[i % 8], green[7 - i % 8], 0, 255 }), $"pixel {i}");
        }
    }

    /// <summary>Verifies rejection of unsupported CPU decoding families.</summary>
    [Test]
    public void Bc4AndBc7_Throw()
    {
        byte[] block = new byte[8];
        Assert.Throws<ImageDecodeException>(
            () => BcDecoder.DecodeLevel(block, 0, DdsDecoder.BcFamily.BC4, 4, 4, 0));
        Assert.Throws<ImageDecodeException>(
            () => BcDecoder.DecodeLevel(block, 0, DdsDecoder.BcFamily.BC7, 4, 4, 0));
    }

    /// <summary>Verifies that truncated payloads fail before block access.</summary>
    [Test]
    public void DecodeLevel_TruncatedPayload_Throws()
    {
        byte[] block = Bc1ColorBlock(0xFFFF, 0xFFFF, 0);
        // A 16x16 BC1 image needs 16 blocks; the payload holds only one.
        Assert.Throws<ImageDecodeException>(
            () => BcDecoder.DecodeLevel(block, 0, DdsDecoder.BcFamily.BC1, 16, 16, 0));
    }

    /// <summary>Verifies mip offsets when decoding a level after the base image.</summary>
    [Test]
    public void DecodeLevel_SecondLevel_SkipsFirstLevelBytes()
    {
        // Level 0 (16x16) holds 16 blocks = 128 bytes; level 1 (8x8) holds 4
        // blocks starting at byte 128. Solid red blocks decode to (255, 0, 0).
        byte[] payload = new byte[128 + 4 * 8];
        ushort red = 0xF800;
        for (int block = 0; block < 4; block++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(128 + block * 8), red);
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(128 + block * 8 + 2), red);
        }

        byte* pixels = BcDecoder.DecodeLevel(payload, 0, DdsDecoder.BcFamily.BC1, 16, 16, 1);
        try
        {
            byte[] result = new byte[8 * 8 * 4];
            new Span<byte>(pixels, result.Length).CopyTo(result.AsSpan());
            for (int i = 0; i < 64; i++)
            {
                Assert.That(result[(i * 4)..(i * 4 + 4)], Is.EqualTo(new byte[] { 255, 0, 0, 255 }), $"pixel {i}");
            }
        }
        finally
        {
            NativeMemory.Free(pixels);
        }
    }
}
