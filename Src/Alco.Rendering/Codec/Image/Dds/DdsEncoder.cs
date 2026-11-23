using System.Buffers.Binary;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Assembles DDS files from BC1-BC5 and BC7 payloads using legacy 128-byte headers (BC1-BC3 UNORM) or 148-byte DX10 headers.
/// </summary>
public static class DdsEncoder
{
    private const int HeaderSize = 128;
    private const int Dx10HeaderSize = 20;

    private const uint DdsdCaps = 0x1;
    private const uint DdsdHeight = 0x2;
    private const uint DdsdWidth = 0x4;
    private const uint DdsdPixelFormat = 0x1000;
    private const uint DdsdMipmapCount = 0x20000;
    private const uint DdsdLinearSize = 0x80000;
    private const uint DdpfFourCc = 0x4;
    private const uint DdscapsTexture = 0x1000;
    private const uint D3d10ResourceDimensionTexture2D = 3;

    private const uint FourCcDxt1 = 0x31545844; // "DXT1"
    private const uint FourCcDxt3 = 0x33545844; // "DXT3"
    private const uint FourCcDxt5 = 0x35545844; // "DXT5"
    private const uint FourCcDx10 = 0x30315844; // "DX10"

    private const uint DxgiBc1Unorm = 71;
    private const uint DxgiBc1UnormSrgb = 72;
    private const uint DxgiBc2Unorm = 73;
    private const uint DxgiBc2UnormSrgb = 74;
    private const uint DxgiBc3Unorm = 77;
    private const uint DxgiBc3UnormSrgb = 78;
    private const uint DxgiBc4Unorm = 80;
    private const uint DxgiBc4Snorm = 81;
    private const uint DxgiBc5Unorm = 83;
    private const uint DxgiBc7Unorm = 98;
    private const uint DxgiBc7UnormSrgb = 99;

    /// <summary>Assembles a DDS file from a block-compressed mip chain identified by <paramref name="family"/>.</summary>
    /// <param name="width">Level-0 width in pixels.</param>
    /// <param name="height">Level-0 height in pixels.</param>
    /// <param name="family">The BC compression family (BC1-BC5 or BC7).</param>
    /// <param name="mipChain">Tightly packed mip chain bytes, level 0 first.</param>
    /// <param name="mipLevels">Number of mip levels in <paramref name="mipChain"/>.</param>
    /// <returns>The complete DDS file bytes.</returns>
    public static byte[] Encode(
        int width,
        int height,
        DdsDecoder.BcFamily family,
        ReadOnlySpan<byte> mipChain,
        int mipLevels)
    {
        Format format = family switch
        {
            DdsDecoder.BcFamily.BC1 => Format.Bc1Unorm,
            DdsDecoder.BcFamily.BC2 => Format.Bc2Unorm,
            DdsDecoder.BcFamily.BC3 => Format.Bc3Unorm,
            DdsDecoder.BcFamily.BC4 => Format.Bc4Unorm,
            DdsDecoder.BcFamily.BC5 => Format.Bc5Rg,
            DdsDecoder.BcFamily.BC7 => Format.Bc7Unorm,
            _ => throw new ImageDecodeException($"DDS encoding is not implemented for {family}."),
        };
        return Encode(width, height, format, mipChain, mipLevels);
    }

    /// <summary>Assembles a DDS file from a block-compressed mip chain matching <paramref name="format"/>.</summary>
    /// <param name="width">Level-0 width in pixels.</param>
    /// <param name="height">Level-0 height in pixels.</param>
    /// <param name="format">The compressed BC format (BC1-BC5 or BC7, UNORM/SNORM/sRGB).</param>
    /// <param name="mipChain">Tightly packed mip chain bytes, level 0 first.</param>
    /// <param name="mipLevels">Number of mip levels in <paramref name="mipChain"/>.</param>
    /// <returns>The complete DDS file bytes.</returns>
    public static byte[] Encode(
        int width,
        int height,
        Format format,
        ReadOnlySpan<byte> mipChain,
        int mipLevels = 1)
    {
        if (!TryMapFormat(format, out DdsDecoder.BcFamily family, out uint legacyFourCc, out uint dxgiFormat, out bool useDx10))
        {
            throw new ImageDecodeException($"DDS encoding is not implemented for format {format}.");
        }
        if (width <= 0 || height <= 0)
        {
            throw new ImageDecodeException($"DDS level-0 dimensions {width}x{height} must be positive.");
        }
        if (mipLevels <= 0)
        {
            throw new ImageDecodeException($"DDS mip level count must be positive, got {mipLevels}.");
        }

        bool isSingleSubBlock = width < 4 && height < 4;
        if (isSingleSubBlock)
        {
            if (mipLevels != 1)
            {
                throw new ImageDecodeException($"DDS sub-block image {width}x{height} supports only 1 mip level, got {mipLevels}.");
            }
        }
        else if (width % 4 != 0 || height % 4 != 0)
        {
            throw new ImageDecodeException($"DDS level-0 dimensions {width}x{height} must be positive multiples of the 4x4 BC block size.");
        }

        uint blockBytes = DdsDecoder.GetBlockBytes(family);
        long chainBytes = 0;
        for (int level = 0; level < mipLevels; level++)
        {
            if (!isSingleSubBlock)
            {
                int levelWidth = Math.Max(1, width >> level);
                int levelHeight = Math.Max(1, height >> level);
                if (levelWidth % 4 != 0 || levelHeight % 4 != 0)
                {
                    throw new ImageDecodeException(
                        $"DDS mip level {level} ({levelWidth}x{levelHeight}) is not a whole number of 4x4 blocks; stop the chain at the previous level.");
                }
            }
            chainBytes += DdsDecoder.GetMipByteCount(width, height, level, blockBytes);
        }
        if (mipChain.Length != chainBytes)
        {
            throw new ImageDecodeException($"The mip chain holds {mipChain.Length} bytes but the {mipLevels}-level chain needs {chainBytes}.");
        }

        uint level0Bytes = DdsDecoder.GetMipByteCount(width, height, 0, blockBytes);
        int totalHeaderSize = HeaderSize + (useDx10 ? Dx10HeaderSize : 0);
        byte[] file = new byte[totalHeaderSize + chainBytes];
        Span<byte> header = file.AsSpan(0, HeaderSize);

        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x20534444); // "DDS " magic
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 124); // header size
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..],
            DdsdCaps | DdsdHeight | DdsdWidth | DdsdPixelFormat | DdsdMipmapCount | DdsdLinearSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], height);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], width);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], level0Bytes);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], mipLevels);

        BinaryPrimitives.WriteUInt32LittleEndian(header[76..], 32); // pixel format size
        BinaryPrimitives.WriteUInt32LittleEndian(header[80..], DdpfFourCc);
        BinaryPrimitives.WriteUInt32LittleEndian(header[84..], useDx10 ? FourCcDx10 : legacyFourCc);
        BinaryPrimitives.WriteUInt32LittleEndian(header[108..], DdscapsTexture);

        if (useDx10)
        {
            Span<byte> dx10 = file.AsSpan(HeaderSize, Dx10HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(dx10, dxgiFormat);
            BinaryPrimitives.WriteUInt32LittleEndian(dx10[4..], D3d10ResourceDimensionTexture2D);
            BinaryPrimitives.WriteUInt32LittleEndian(dx10[8..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(dx10[12..], 1);
            BinaryPrimitives.WriteUInt32LittleEndian(dx10[16..], 0);
        }

        mipChain.CopyTo(file.AsSpan(totalHeaderSize));
        return file;
    }

    private static bool TryMapFormat(
        Format format,
        out DdsDecoder.BcFamily family,
        out uint legacyFourCc,
        out uint dxgiFormat,
        out bool useDx10)
    {
        switch (format)
        {
            case Format.Bc1Unorm:
                family = DdsDecoder.BcFamily.BC1;
                legacyFourCc = FourCcDxt1;
                dxgiFormat = DxgiBc1Unorm;
                useDx10 = false;
                return true;
            case Format.Bc1Srgb:
                family = DdsDecoder.BcFamily.BC1;
                legacyFourCc = 0;
                dxgiFormat = DxgiBc1UnormSrgb;
                useDx10 = true;
                return true;
            case Format.Bc2Unorm:
                family = DdsDecoder.BcFamily.BC2;
                legacyFourCc = FourCcDxt3;
                dxgiFormat = DxgiBc2Unorm;
                useDx10 = false;
                return true;
            case Format.Bc2Srgb:
                family = DdsDecoder.BcFamily.BC2;
                legacyFourCc = 0;
                dxgiFormat = DxgiBc2UnormSrgb;
                useDx10 = true;
                return true;
            case Format.Bc3Unorm:
                family = DdsDecoder.BcFamily.BC3;
                legacyFourCc = FourCcDxt5;
                dxgiFormat = DxgiBc3Unorm;
                useDx10 = false;
                return true;
            case Format.Bc3Srgb:
                family = DdsDecoder.BcFamily.BC3;
                legacyFourCc = 0;
                dxgiFormat = DxgiBc3UnormSrgb;
                useDx10 = true;
                return true;
            case Format.Bc4Unorm:
                family = DdsDecoder.BcFamily.BC4;
                legacyFourCc = 0;
                dxgiFormat = DxgiBc4Unorm;
                useDx10 = true;
                return true;
            case Format.Bc4Snorm:
                family = DdsDecoder.BcFamily.BC4;
                legacyFourCc = 0;
                dxgiFormat = DxgiBc4Snorm;
                useDx10 = true;
                return true;
            case Format.Bc5Rg:
                family = DdsDecoder.BcFamily.BC5;
                legacyFourCc = 0;
                dxgiFormat = DxgiBc5Unorm;
                useDx10 = true;
                return true;
            case Format.Bc7Unorm:
                family = DdsDecoder.BcFamily.BC7;
                legacyFourCc = 0;
                dxgiFormat = DxgiBc7Unorm;
                useDx10 = true;
                return true;
            case Format.Bc7Srgb:
                family = DdsDecoder.BcFamily.BC7;
                legacyFourCc = 0;
                dxgiFormat = DxgiBc7UnormSrgb;
                useDx10 = true;
                return true;
            default:
                family = default;
                legacyFourCc = 0;
                dxgiFormat = 0;
                useDx10 = false;
                return false;
        }
    }
}
