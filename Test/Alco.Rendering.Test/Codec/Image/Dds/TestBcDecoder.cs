using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NUnit.Framework;

namespace Alco.Rendering.Test;

using Alco.Rendering;

/// <summary>
/// CPU fallback decode of block-compressed payloads: hand-crafted blocks cover the
/// BC1 color modes (including punchthrough), the BC2/BC3 alpha codings, and the
/// rejected BC4-BC7 families.
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

    [Test]
    public void Bc1_FourColorMode_DecodesBothEndpoints()
    {
        // color0 > color1 selects the 4-color mode; pixel 1 carries index 1 (color1).
        byte[] pixels = DecodeSingleBlock(
            Bc1ColorBlock(0xFFFF, 0x0000, 1u << 2),
            DdsDecoder.BcFamily.BC1);

        Assert.That(pixels[0..4], Is.EqualTo(new byte[] { 255, 255, 255, 255 }));
        Assert.That(pixels[4..8], Is.EqualTo(new byte[] { 0, 0, 0, 255 }));
    }

    [Test]
    public void Bc1_ThreeColorMode_Index3IsTransparent()
    {
        // color0 < color1 selects the punchthrough mode: index 3 decodes to
        // transparent black while index 0 keeps color 0 opaque.
        byte[] pixels = DecodeSingleBlock(
            Bc1ColorBlock(0x0000, 0xFFFF, 3u << 6),
            DdsDecoder.BcFamily.BC1);

        Assert.That(pixels[0..4], Is.EqualTo(new byte[] { 0, 0, 0, 255 }));       // index 0 = color0
        Assert.That(pixels[4..8], Is.EqualTo(new byte[] { 0, 0, 0, 255 }));       // pixel 1, index 0
        Assert.That(pixels[8..12], Is.EqualTo(new byte[] { 0, 0, 0, 255 }));      // pixel 2, index 0
        Assert.That(pixels[12..16], Is.EqualTo(new byte[] { 0, 0, 0, 0 }));       // pixel 3, index 3 = punchthrough
    }

    [Test]
    public void Bc1_InterpolatedColors_MatchIntegerMix()
    {
        // 0xF800 (red) > 0x07E0 (green) selects the 4-color mode. RGB565 channel
        // replication expands the endpoints to (255,0,0) and (0,255,0); index 2
        // is the integer mix (2*c0 + c1) / 3 = (170, 85, 0).
        byte[] pixels = DecodeSingleBlock(
            Bc1ColorBlock(0xF800, 0x07E0, 2u << 2),
            DdsDecoder.BcFamily.BC1);

        Assert.That(pixels[0..4], Is.EqualTo(new byte[] { 255, 0, 0, 255 }));
        Assert.That(pixels[4..8], Is.EqualTo(new byte[] { 170, 85, 0, 255 }));
    }

    [Test]
    public void Bc3_AlphaInterpolation_MatchesEightValuePalette()
    {
        // a0=255 > a1=0: palette[j] = ((8-j)*255 + (j-1)*0)/7. Pixel 1 carries
        // alpha index 3 ((5*255 + 2*0)/7 = 182); pixel 0 carries index 0 (255).
        ulong alphaIndices = 3UL << 3;
        byte[] pixels = DecodeSingleBlock(
            Bc3Block(255, 0, alphaIndices, 0xFFFF, 0xFFFF, 0),
            DdsDecoder.BcFamily.BC3);

        Assert.That(pixels[3], Is.EqualTo(255));
        Assert.That(pixels[7], Is.EqualTo(182));
    }

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

    [Test]
    public void Bc3_ThreeColorBlock_Index3RepeatsColor0WithContainerAlpha()
    {
        // color0 < color1 without punchthrough: index 3 repeats color 0 and the
        // alpha comes from the DXT5 block, not from the color block. Pixel 3
        // carries color index 3; with a0=64/a1=255 its alpha is index 0 (= 64) —
        // a punchthrough decode would wrongly yield 0. Pixel 0 carries alpha
        // index 7 (= 255, the 6-value palette's explicit white entry).
        ulong alphaIndices = 7UL;
        byte[] pixels = DecodeSingleBlock(
            Bc3Block(64, 255, alphaIndices, 0x0000, 0xFFFF, 3u << 6),
            DdsDecoder.BcFamily.BC3);

        Assert.That(pixels[0..4], Is.EqualTo(new byte[] { 0, 0, 0, 255 }));   // index 0 = color0, alpha 255
        Assert.That(pixels[12..16], Is.EqualTo(new byte[] { 0, 0, 0, 64 }));  // index 3 = color0, alpha 64
    }

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

    [Test]
    public void Bc4AndBc7_Throw()
    {
        byte[] block = new byte[8];
        Assert.Throws<ImageDecodeException>(
            () => BcDecoder.DecodeLevel(block, 0, DdsDecoder.BcFamily.BC4, 4, 4, 0));
        Assert.Throws<ImageDecodeException>(
            () => BcDecoder.DecodeLevel(block, 0, DdsDecoder.BcFamily.BC7, 4, 4, 0));
    }

    [Test]
    public void DecodeLevel_TruncatedPayload_Throws()
    {
        byte[] block = Bc1ColorBlock(0xFFFF, 0xFFFF, 0);
        // A 16x16 BC1 image needs 16 blocks; the payload holds only one.
        Assert.Throws<ImageDecodeException>(
            () => BcDecoder.DecodeLevel(block, 0, DdsDecoder.BcFamily.BC1, 16, 16, 0));
    }

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
