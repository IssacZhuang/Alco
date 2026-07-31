using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Parser for DDS (DirectDraw Surface) files holding block-compressed textures (BC1-BC7).
/// No pixel decoding happens: the BC blocks are uploaded to the GPU verbatim, including
/// the mip chain stored in the file. Uncompressed DDS pixel formats are not supported.
/// <br/>All methods are thread-safe (pure read-only span access, no allocations).
/// </summary>
internal static class DdsDecoder
{
    /// <summary>The "DDS " file magic, little endian.</summary>
    private const uint Magic = 0x20534444;

    private const int HeaderSize = 128;
    private const int Dx10HeaderSize = 20;

    private const uint PixelFormatFlagFourCc = 0x4;

    // fourCC codes as little endian uints.
    private const uint FourCcDxt1 = 0x31545844; // "DXT1"
    private const uint FourCcDxt3 = 0x33545844; // "DXT3"
    private const uint FourCcDxt5 = 0x35545844; // "DXT5"
    private const uint FourCcAti1 = 0x31495441; // "ATI1"
    private const uint FourCcBc4u = 0x55344342; // "BC4U"
    private const uint FourCcAti2 = 0x32495441; // "ATI2"
    private const uint FourCcBc5u = 0x55354342; // "BC5U"
    private const uint FourCcDx10 = 0x30315844; // "DX10"

    // DXGI formats carried by the DX10 extended header.
    private const uint DxgiBc1Unorm = 71;
    private const uint DxgiBc1UnormSrgb = 72;
    private const uint DxgiBc2Unorm = 73;
    private const uint DxgiBc2UnormSrgb = 74;
    private const uint DxgiBc3Unorm = 77;
    private const uint DxgiBc3UnormSrgb = 78;
    private const uint DxgiBc4Unorm = 80;
    private const uint DxgiBc5Unorm = 83;
    private const uint DxgiBc6hUf16 = 95;
    private const uint DxgiBc6hSf16 = 96;
    private const uint DxgiBc7Unorm = 98;
    private const uint DxgiBc7UnormSrgb = 99;

    /// <summary>The BC compression family of a DDS pixel payload.</summary>
    internal enum BcFamily
    {
        BC1,
        BC2,
        BC3,
        BC4,
        BC5,
        BC6H,
        BC7,
    }

    /// <summary>Check whether the data starts with the DDS file magic.</summary>
    public static bool IsDds(ReadOnlySpan<byte> data)
        => data.Length >= 4 && MemoryMarshal.Read<uint>(data) == Magic;

    /// <summary>
    /// Parse and validate a DDS file. The mip chain is stored contiguously at
    /// <paramref name="dataOffset"/>, level 0 first, each level a tightly packed
    /// sequence of 4x4 blocks (see <see cref="GetMipByteCount"/>).
    /// </summary>
    /// <param name="data">Complete DDS file bytes.</param>
    /// <param name="srgb">
    /// Pick the sRGB variant of the file's BC format (for albedo textures). The caller
    /// decides because legacy headers carry no color space information; ignored for
    /// BC4-BC6H which have no sRGB variants.
    /// </param>
    /// <param name="format">The engine pixel format matching the file's BC blocks.</param>
    /// <param name="width">Level-0 width in pixels.</param>
    /// <param name="height">Level-0 height in pixels.</param>
    /// <param name="mipLevels">
    /// The number of usable mip levels: the file's chain truncated at the first level
    /// whose extent is not a whole number of 4x4 blocks (the GPU requires block-aligned
    /// copies). Always at least 1.
    /// </param>
    /// <param name="dataOffset">Byte offset of the mip chain inside <paramref name="data"/>.</param>
    /// <remarks>Sub-block level-0 dimensions (below 4x4, e.g. placeholder 1x1 maps) are
    /// reported as 4x4 with a single level: the one stored block becomes the whole texture.</remarks>
    /// <exception cref="ImageDecodeException">Invalid, truncated, uncompressed or unsupported DDS data.</exception>
    public static void Decode(
        ReadOnlySpan<byte> data,
        bool srgb,
        out PixelFormat format,
        out int width,
        out int height,
        out int mipLevels,
        out int dataOffset)
    {
        if (!IsDds(data) || data.Length < HeaderSize)
        {
            throw new ImageDecodeException("Not a DDS file or the header is truncated.");
        }
        if (MemoryMarshal.Read<uint>(data[4..]) != 124)
        {
            throw new ImageDecodeException("Invalid DDS header size, expected 124.");
        }

        height = MemoryMarshal.Read<int>(data[12..]);
        width = MemoryMarshal.Read<int>(data[16..]);
        if (width <= 0 || height <= 0)
        {
            throw new ImageDecodeException($"Invalid DDS dimensions {width}x{height}.");
        }
        mipLevels = MemoryMarshal.Read<int>(data[28..]);
        if (mipLevels <= 0)
        {
            mipLevels = 1;
        }

        uint pixelFormatFlags = MemoryMarshal.Read<uint>(data[80..]);
        if ((pixelFormatFlags & PixelFormatFlagFourCc) == 0)
        {
            throw new ImageDecodeException("Uncompressed DDS pixel formats are not supported; use BC1-BC7.");
        }

        uint fourCc = MemoryMarshal.Read<uint>(data[84..]);
        dataOffset = HeaderSize;
        BcFamily family;
        switch (fourCc)
        {
            case FourCcDxt1: family = BcFamily.BC1; break;
            case FourCcDxt3: family = BcFamily.BC2; break;
            case FourCcDxt5: family = BcFamily.BC3; break;
            case FourCcAti1:
            case FourCcBc4u: family = BcFamily.BC4; break;
            case FourCcAti2:
            case FourCcBc5u: family = BcFamily.BC5; break;
            case FourCcDx10:
                family = ParseDx10Format(data, out dataOffset);
                break;
            default:
                throw new ImageDecodeException($"Unsupported DDS fourCC '{FourCcToString(fourCc)}'; expected DXT1/DXT3/DXT5/ATI1/ATI2/DX10.");
        }

        format = ToPixelFormat(family, srgb);

        if (width < 4 && height < 4)
        {
            // Sub-block images (e.g. 1x1 placeholder maps) hold exactly one BC block.
            // The GPU texture is created as a full 4x4 block because wgpu queue writes
            // must be block-aligned; sampling any texel gives the placeholder's color.
            width = 4;
            height = 4;
            mipLevels = 1;
        }
        else
        {
            // Use only the leading mip levels whose extents are whole 4x4 blocks: the GPU
            // upload path (wgpu queue writes) requires block-multiple copy extents, which
            // deep sub-4-pixel or unaligned levels can never satisfy. The sampler simply
            // clamps to the smallest uploaded level, so truncating the chain is safe.
            int usableLevels = 0;
            while (usableLevels < mipLevels)
            {
                int levelWidth = Math.Max(1, width >> usableLevels);
                int levelHeight = Math.Max(1, height >> usableLevels);
                if (levelWidth % 4 != 0 || levelHeight % 4 != 0)
                {
                    break;
                }
                usableLevels++;
            }
            if (usableLevels == 0)
            {
                throw new ImageDecodeException($"DDS level-0 dimensions {width}x{height} are not multiples of the 4x4 BC block size.");
            }
            mipLevels = usableLevels;
        }

        // The file must hold the whole used mip chain.
        uint blockBytes = GetBlockBytes(family);
        long required = dataOffset;
        for (int level = 0; level < mipLevels; level++)
        {
            required += GetMipByteCount(width, height, level, blockBytes);
        }
        if (data.Length < required)
        {
            throw new ImageDecodeException($"Truncated DDS file: {data.Length} bytes, the {mipLevels}-level mip chain needs {required}.");
        }
    }

    /// <summary>The byte size of one mip level: 4x4 blocks, clamped to one block.</summary>
    public static uint GetMipByteCount(int width, int height, int level, uint blockBytes)
    {
        uint blocksWide = (uint)((Math.Max(1, width >> level) + 3) / 4);
        uint blocksHigh = (uint)((Math.Max(1, height >> level) + 3) / 4);
        return blocksWide * blocksHigh * blockBytes;
    }

    /// <summary>Bytes per 4x4 block: 8 for BC1/BC4, 16 for all others.</summary>
    public static uint GetBlockBytes(BcFamily family)
        => family is BcFamily.BC1 or BcFamily.BC4 ? 8u : 16u;

    private static BcFamily ParseDx10Format(ReadOnlySpan<byte> data, out int dataOffset)
    {
        if (data.Length < HeaderSize + Dx10HeaderSize)
        {
            throw new ImageDecodeException("Truncated DDS DX10 extended header.");
        }
        dataOffset = HeaderSize + Dx10HeaderSize;
        uint dxgiFormat = MemoryMarshal.Read<uint>(data[128..]);
        return dxgiFormat switch
        {
            DxgiBc1Unorm or DxgiBc1UnormSrgb => BcFamily.BC1,
            DxgiBc2Unorm or DxgiBc2UnormSrgb => BcFamily.BC2,
            DxgiBc3Unorm or DxgiBc3UnormSrgb => BcFamily.BC3,
            DxgiBc4Unorm => BcFamily.BC4,
            DxgiBc5Unorm => BcFamily.BC5,
            DxgiBc6hUf16 or DxgiBc6hSf16 => BcFamily.BC6H,
            DxgiBc7Unorm or DxgiBc7UnormSrgb => BcFamily.BC7,
            _ => throw new ImageDecodeException($"Unsupported DDS DXGI format {dxgiFormat}; expected a BC1-BC7 uncompressed value."),
        };
    }

    private static PixelFormat ToPixelFormat(BcFamily family, bool srgb) => family switch
    {
        BcFamily.BC1 => srgb ? PixelFormat.BC1RGBAUnormSrgb : PixelFormat.BC1RGBAUnorm,
        BcFamily.BC2 => srgb ? PixelFormat.BC2RGBAUnormSrgb : PixelFormat.BC2RGBAUnorm,
        BcFamily.BC3 => srgb ? PixelFormat.BC3RGBAUnormSrgb : PixelFormat.BC3RGBAUnorm,
        BcFamily.BC4 => PixelFormat.BC4RUnorm,
        BcFamily.BC5 => PixelFormat.BC5RGUnorm,
        BcFamily.BC6H => PixelFormat.BC6HRGBUfloat,
        BcFamily.BC7 => srgb ? PixelFormat.BC7RGBAUnormSrgb : PixelFormat.BC7RGBAUnorm,
        _ => throw new ImageDecodeException($"Unsupported DDS BC family {family}."),
    };

    private static string FourCcToString(uint fourCc)
    {
        Span<char> chars = stackalloc char[4];
        for (int i = 0; i < 4; i++)
        {
            chars[i] = (char)((fourCc >> (i * 8)) & 0xFF);
        }
        return new string(chars);
    }
}
