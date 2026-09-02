using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Alco.Rendering;

/// <summary>
/// CPU decoder for block-compressed DDS payloads (BC1-BC3): decompresses a single
/// mip level to an RGBA8 pixel buffer. Fallback path for devices without
/// <see cref="Alco.Graphics.GPUFeatures.TextureCompressionBC"/>; the asset pipeline
/// only produces BC1 and BC3, so BC4-BC7 are rejected.
/// <br/>All methods are thread-safe (pure read-only span access).
/// </summary>
/// <remarks>
/// Public for the TextureConverter tool (round-trip verification); the runtime
/// fallback path in RenderingSystem uses it in-process.
/// </remarks>
public static unsafe class BcDecoder
{
    /// <summary>
    /// Decompress one mip level of a block-compressed DDS payload to RGBA8 pixels.
    /// </summary>
    /// <param name="fileData">The complete DDS file bytes.</param>
    /// <param name="dataOffset">Byte offset of the mip chain (level 0) inside <paramref name="fileData"/>.</param>
    /// <param name="family">The BC compression family of the payload.</param>
    /// <param name="width">Level-0 width in pixels.</param>
    /// <param name="height">Level-0 height in pixels.</param>
    /// <param name="level">The mip level to decode (0 is the largest).</param>
    /// <returns>Pointer to the level's RGBA8 pixels (<c>levelWidth * levelHeight * 4</c> bytes).
    /// Caller must free via <c>NativeMemory.Free</c>.</returns>
    /// <exception cref="ImageDecodeException">Unsupported BC family, or the file is truncated.</exception>
    public static byte* DecodeLevel(
        ReadOnlySpan<byte> fileData,
        int dataOffset,
        DdsDecoder.BcFamily family,
        int width,
        int height,
        int level)
    {
        if (family is not (DdsDecoder.BcFamily.BC1 or DdsDecoder.BcFamily.BC2 or DdsDecoder.BcFamily.BC3))
        {
            throw new ImageDecodeException($"CPU fallback decode is not implemented for {family} textures.");
        }

        uint blockBytes = DdsDecoder.GetBlockBytes(family);

        // Skip the levels before the requested one.
        long offset = dataOffset;
        for (int i = 0; i < level; i++)
        {
            offset += DdsDecoder.GetMipByteCount(width, height, i, blockBytes);
        }
        uint levelBytes = DdsDecoder.GetMipByteCount(width, height, level, blockBytes);
        if (offset + levelBytes > fileData.Length)
        {
            throw new ImageDecodeException(
                $"Truncated DDS file: level {level} needs bytes [{offset}, {offset + levelBytes}) but the file is {fileData.Length} bytes.");
        }

        int levelWidth = Math.Max(1, width >> level);
        int levelHeight = Math.Max(1, height >> level);
        int blocksWide = (levelWidth + 3) / 4;
        int blocksHigh = (levelHeight + 3) / 4;

        byte* output = (byte*)NativeMemory.Alloc((nuint)(levelWidth * (long)levelHeight * 4));
        ReadOnlySpan<byte> blocks = fileData.Slice((int)offset, (int)levelBytes);

        // Reused 4x4 decompressed tile, row-major RGBA.
        Span<byte> tile = stackalloc byte[4 * 4 * 4];

        for (int blockY = 0; blockY < blocksHigh; blockY++)
        {
            for (int blockX = 0; blockX < blocksWide; blockX++)
            {
                ReadOnlySpan<byte> block = blocks.Slice(
                    (blockY * blocksWide + blockX) * (int)blockBytes,
                    (int)blockBytes);

                DecodeBlock(block, family, tile);

                // Copy the tile, clipped to the level's extent: the last blocks of
                // levels that are not a whole number of blocks overshoot the image.
                int copyWidth = Math.Min(4, levelWidth - blockX * 4);
                int copyHeight = Math.Min(4, levelHeight - blockY * 4);
                for (int y = 0; y < copyHeight; y++)
                {
                    int destOffset = ((blockY * 4 + y) * levelWidth + blockX * 4) * 4;
                    tile.Slice(y * 4 * 4, copyWidth * 4).CopyTo(new Span<byte>(output + destOffset, copyWidth * 4));
                }
            }
        }

        return output;
    }

    /// <summary>Decompress a single BC1/BC2/BC3 block into a 64-byte RGBA tile.</summary>
    private static void DecodeBlock(ReadOnlySpan<byte> block, DdsDecoder.BcFamily family, Span<byte> tile)
    {
        if (family == DdsDecoder.BcFamily.BC1)
        {
            DecodeBc1Colors(block, punchthrough: true, tile);
            return;
        }

        // BC2/BC3: 8 alpha bytes followed by the color block. The color decode never
        // punches alpha through; a 3-color block maps code 3 to color 0 (s3tc spec).
        DecodeBc1Colors(block[8..], punchthrough: false, tile);

        if (family == DdsDecoder.BcFamily.BC2)
        {
            for (int i = 0; i < 16; i++)
            {
                // 4-bit alpha, packed two pixels per byte, low nibble first.
                byte nibble = (byte)((block[i / 2] >> ((i % 2) * 4)) & 0xF);
                tile[i * 4 + 3] = (byte)(nibble * 17);
            }
            return;
        }

        // BC3: 8-bit interpolated alpha (DXT5).
        byte a0 = block[0];
        byte a1 = block[1];
        Span<byte> palette = stackalloc byte[8];
        palette[0] = a0;
        palette[1] = a1;
        if (a0 > a1)
        {
            for (int i = 1; i < 7; i++)
            {
                palette[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
            }
        }
        else
        {
            for (int i = 1; i < 5; i++)
            {
                palette[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
            }
            palette[6] = 0;
            palette[7] = 0xFF;
        }

        // 48 bits of 3-bit indices, LSB-first, starting after the two endpoint bytes.
        for (int i = 0; i < 16; i++)
        {
            int bit = i * 3;
            int byteIndex = 2 + bit / 8;
            int window = block[byteIndex]
                | (byteIndex + 1 < 8 ? block[byteIndex + 1] << 8 : 0)
                | (byteIndex + 2 < 8 ? block[byteIndex + 2] << 16 : 0);
            tile[i * 4 + 3] = palette[(window >> (bit % 8)) & 0x7];
        }
    }

    /// <summary>
    /// Decompress the 8-byte BC1 color block into the tile. With
    /// <paramref name="punchthrough"/> (BC1), a 3-color block maps index 3 to
    /// transparent black; without it (BC2/BC3) index 3 maps to color 0 and alpha is
    /// left untouched for the container's own alpha channel.
    /// </summary>
    private static void DecodeBc1Colors(ReadOnlySpan<byte> colors, bool punchthrough, Span<byte> tile)
    {
        ushort color0 = BinaryPrimitives.ReadUInt16LittleEndian(colors);
        ushort color1 = BinaryPrimitives.ReadUInt16LittleEndian(colors[2..]);
        uint indices = BinaryPrimitives.ReadUInt32LittleEndian(colors[4..]);

        Span<byte> palette = stackalloc byte[4 * 4]; // Four RGBA colors.
        WriteRgb565(palette, 0, color0);
        WriteRgb565(palette, 4, color1);
        bool fourColorMode = color0 > color1;
        if (fourColorMode)
        {
            MixChannel(palette, 0, 4, 8, 2, 1);
            MixChannel(palette, 0, 4, 12, 1, 2);
            // The BC1 palette order is 0=c0, 1=(2c0+c1)/3, 2=(c0+2c1)/3, 3=c1:
            // rotate the freshly mixed colors and the c1 endpoint into place.
            Span<byte> c1 = stackalloc byte[4];
            palette.Slice(4, 4).CopyTo(c1);
            palette.Slice(8, 4).CopyTo(palette.Slice(4, 4));
            palette.Slice(12, 4).CopyTo(palette.Slice(8, 4));
            c1.CopyTo(palette.Slice(12, 4));
        }
        else
        {
            MixChannel(palette, 0, 4, 8, 1, 1);
            palette[0..4].CopyTo(palette[12..16]); // Code 3 repeats color 0.
        }

        for (int i = 0; i < 16; i++)
        {
            int index = (int)((indices >> (i * 2)) & 0x3);
            tile[i * 4 + 0] = palette[index * 4 + 0];
            tile[i * 4 + 1] = palette[index * 4 + 1];
            tile[i * 4 + 2] = palette[index * 4 + 2];
            tile[i * 4 + 3] = punchthrough && !fourColorMode && index == 3 ? (byte)0 : palette[index * 4 + 3];
        }
    }

    /// <summary>Expand an RGB565 value into the RGBA slot at <paramref name="offset"/> (alpha set to opaque).</summary>
    private static void WriteRgb565(Span<byte> palette, int offset, ushort value)
    {
        int r = (value >> 11) & 0x1F;
        int g = (value >> 5) & 0x3F;
        int b = value & 0x1F;
        palette[offset + 0] = (byte)((r << 3) | (r >> 2));
        palette[offset + 1] = (byte)((g << 2) | (g >> 4));
        palette[offset + 2] = (byte)((b << 3) | (b >> 2));
        palette[offset + 3] = 0xFF;
    }

    /// <summary>
    /// Interpolate a palette entry from the two endpoint colors:
    /// <c>target = (weightA * a + weightB * b) / (weightA + weightB)</c>.
    /// </summary>
    private static void MixChannel(Span<byte> palette, int a, int b, int target, int weightA, int weightB)
    {
        int divisor = weightA + weightB;
        for (int c = 0; c < 3; c++)
        {
            palette[target + c] = (byte)((palette[a + c] * weightA + palette[b + c] * weightB) / divisor);
        }
        palette[target + 3] = 0xFF;
    }
}
