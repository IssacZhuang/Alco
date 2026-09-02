using System.Buffers.Binary;

namespace Alco.Rendering;

/// <summary>
/// Assembles DDS files from block-compressed payloads: a legacy 128-byte header
/// (fourCC DXT1/DXT3/DXT5, no DX10 extension) followed by the mip chain, level 0
/// first. The output is byte-compatible with <see cref="DdsDecoder.ParseHeader"/>.
/// <br/>All methods are thread-safe (pure span access).
/// </summary>
public static class DdsEncoder
{
    private const int HeaderSize = 128;

    private const uint DdsdCaps = 0x1;
    private const uint DdsdHeight = 0x2;
    private const uint DdsdWidth = 0x4;
    private const uint DdsdPixelFormat = 0x1000;
    private const uint DdsdMipmapCount = 0x20000;
    private const uint DdsdLinearSize = 0x80000;
    private const uint DdpfFourCc = 0x4;
    private const uint DdscapsTexture = 0x1000;

    private const uint FourCcDxt1 = 0x31545844; // "DXT1"
    private const uint FourCcDxt3 = 0x33545844; // "DXT3"
    private const uint FourCcDxt5 = 0x35545844; // "DXT5"

    /// <summary>
    /// Assemble a DDS file from a block-compressed mip chain.
    /// </summary>
    /// <param name="width">Level-0 width in pixels; must be a multiple of 4.</param>
    /// <param name="height">Level-0 height in pixels; must be a multiple of 4.</param>
    /// <param name="family">The BC compression family; only BC1-BC3 are encodable.</param>
    /// <param name="mipChain">The tightly packed mip chain bytes, level 0 first.</param>
    /// <param name="mipLevels">The number of mip levels in <paramref name="mipChain"/>;
    /// every level extent must be a whole number of 4x4 blocks.</param>
    /// <returns>The complete DDS file bytes.</returns>
    /// <exception cref="ImageDecodeException">Invalid dimensions, family, or mip
    /// chain length.</exception>
    public static byte[] Encode(
        int width,
        int height,
        DdsDecoder.BcFamily family,
        ReadOnlySpan<byte> mipChain,
        int mipLevels)
    {
        if (family is not (DdsDecoder.BcFamily.BC1 or DdsDecoder.BcFamily.BC2 or DdsDecoder.BcFamily.BC3))
        {
            throw new ImageDecodeException($"DDS encoding is not implemented for {family}.");
        }
        if (width <= 0 || height <= 0 || width % 4 != 0 || height % 4 != 0)
        {
            throw new ImageDecodeException($"DDS level-0 dimensions {width}x{height} must be positive multiples of the 4x4 BC block size.");
        }
        if (mipLevels <= 0)
        {
            throw new ImageDecodeException($"DDS mip level count must be positive, got {mipLevels}.");
        }

        uint blockBytes = DdsDecoder.GetBlockBytes(family);
        long chainBytes = 0;
        for (int level = 0; level < mipLevels; level++)
        {
            int levelWidth = Math.Max(1, width >> level);
            int levelHeight = Math.Max(1, height >> level);
            if (levelWidth % 4 != 0 || levelHeight % 4 != 0)
            {
                throw new ImageDecodeException(
                    $"DDS mip level {level} ({levelWidth}x{levelHeight}) is not a whole number of 4x4 blocks; stop the chain at the previous level.");
            }
            chainBytes += DdsDecoder.GetMipByteCount(width, height, level, blockBytes);
        }
        if (mipChain.Length != chainBytes)
        {
            throw new ImageDecodeException($"The mip chain holds {mipChain.Length} bytes but the {mipLevels}-level chain needs {chainBytes}.");
        }

        uint level0Bytes = DdsDecoder.GetMipByteCount(width, height, 0, blockBytes);
        byte[] file = new byte[HeaderSize + chainBytes];
        Span<byte> header = file.AsSpan(0, HeaderSize);

        BinaryPrimitives.WriteUInt32LittleEndian(header, 0x20534444);            // "DDS " magic
        BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 124);              // header size
        BinaryPrimitives.WriteUInt32LittleEndian(header[8..],
            DdsdCaps | DdsdHeight | DdsdWidth | DdsdPixelFormat | DdsdMipmapCount | DdsdLinearSize);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], height);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], width);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..], level0Bytes);     // linear size
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], mipLevels);

        BinaryPrimitives.WriteUInt32LittleEndian(header[76..], 32);              // pixel format size
        BinaryPrimitives.WriteUInt32LittleEndian(header[80..], DdpfFourCc);
        BinaryPrimitives.WriteUInt32LittleEndian(header[84..], FourCc(family));
        BinaryPrimitives.WriteUInt32LittleEndian(header[108..], DdscapsTexture);

        mipChain.CopyTo(file.AsSpan(HeaderSize));
        return file;
    }

    private static uint FourCc(DdsDecoder.BcFamily family) => family switch
    {
        DdsDecoder.BcFamily.BC1 => FourCcDxt1,
        DdsDecoder.BcFamily.BC2 => FourCcDxt3,
        DdsDecoder.BcFamily.BC3 => FourCcDxt5,
        _ => throw new ImageDecodeException($"DDS encoding is not implemented for {family}."),
    };
}
