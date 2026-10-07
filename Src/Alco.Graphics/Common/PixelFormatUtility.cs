namespace Alco.Graphics;

/// <summary>
/// Provides pixel-format queries: pixel and block sizes, sRGB, stencil and depth checks.
/// </summary>
public static class PixelFormatUtility
{
    /// <summary>
    /// Whether the pixel format is an sRGB variant.
    /// </summary>
    public static bool IsSrgbFormat(PixelFormat format)
    {
        return format switch
        {
            PixelFormat.RGBA8UnormSrgb => true,
            PixelFormat.BGRA8UnormSrgb => true,
            PixelFormat.ASTC4x4UnormSrgb => true,
            PixelFormat.ASTC5x4UnormSrgb => true,
            PixelFormat.ASTC5x5UnormSrgb => true,
            PixelFormat.ASTC6x5UnormSrgb => true,
            PixelFormat.ASTC6x6UnormSrgb => true,
            PixelFormat.ASTC8x5UnormSrgb => true,
            PixelFormat.ASTC8x6UnormSrgb => true,
            PixelFormat.ASTC8x8UnormSrgb => true,
            PixelFormat.ASTC10x5UnormSrgb => true,
            PixelFormat.ASTC10x6UnormSrgb => true,
            PixelFormat.ASTC10x8UnormSrgb => true,
            PixelFormat.ASTC10x10UnormSrgb => true,
            PixelFormat.ASTC12x10UnormSrgb => true,
            PixelFormat.ASTC12x12UnormSrgb => true,
            PixelFormat.BC1RGBAUnormSrgb => true,
            PixelFormat.BC2RGBAUnormSrgb => true,
            PixelFormat.BC3RGBAUnormSrgb => true,
            PixelFormat.BC7RGBAUnormSrgb => true,
            PixelFormat.ETC2RGB8UnormSrgb => true,
            PixelFormat.ETC2RGB8A1UnormSrgb => true,
            PixelFormat.ETC2RGBA8UnormSrgb => true,
            _ => false,
        };
    }

    /// <summary>
    /// Whether the pixel format has a stencil component.
    /// </summary>
    public static bool HasStencil(PixelFormat format)
    {
        return format == PixelFormat.Depth24PlusStencil8 || format == PixelFormat.Depth32FloatStencil8;
    }

    /// <summary>
    /// Whether the pixel format is a depth or depth-stencil format.
    /// </summary>
    public static bool IsDepthFormat(PixelFormat format)
    {
        return format is PixelFormat.Depth16Unorm
            or PixelFormat.Depth24Plus
            or PixelFormat.Depth24PlusStencil8
            or PixelFormat.Depth32Float
            or PixelFormat.Depth32FloatStencil8;
    }

    /// <summary>
    /// Returns the size of one pixel in bytes.
    /// </summary>
    /// <param name="format">The pixel format to query.</param>
    /// <param name="size">When this method returns, the size of one pixel in bytes, or 0 if the format is compressed or unknown.</param>
    /// <returns>True if the pixel size was retrieved, false otherwise.</returns>
    public static bool TryGetPixelSize(PixelFormat format, out uint size)
    {
        // returns false for compressed/unknown formats; use TryGetCompressedBlockSize for those
        switch (format)
        {
            case PixelFormat.R8Unorm:
            case PixelFormat.R8Snorm:
            case PixelFormat.R8Uint:
            case PixelFormat.R8Sint:
                size= 1;
                return true;
            case PixelFormat.R16Uint:
            case PixelFormat.R16Sint:
            case PixelFormat.R16Float:
            case PixelFormat.RG8Unorm:
            case PixelFormat.RG8Snorm:
            case PixelFormat.RG8Uint:
            case PixelFormat.RG8Sint:
                size = 2;
                return true;
            case PixelFormat.R32Float:
            case PixelFormat.R32Uint:
            case PixelFormat.R32Sint:
            case PixelFormat.RG16Uint:
            case PixelFormat.RG16Sint:
            case PixelFormat.RG16Float:
            case PixelFormat.RGBA8Unorm:
            case PixelFormat.RGBA8UnormSrgb:
            case PixelFormat.RGBA8Snorm:
            case PixelFormat.RGBA8Uint:
            case PixelFormat.RGBA8Sint:
            case PixelFormat.BGRA8Unorm:
            case PixelFormat.BGRA8UnormSrgb:
            case PixelFormat.RGB10A2Uint:
            case PixelFormat.RGB10A2Unorm:
            case PixelFormat.RG11B10Ufloat:
            case PixelFormat.RGB9E5Ufloat:
                size = 4;
                return true;
            case PixelFormat.RG32Float:
            case PixelFormat.RG32Uint:
            case PixelFormat.RG32Sint:
            case PixelFormat.RGBA16Uint:
            case PixelFormat.RGBA16Sint:
            case PixelFormat.RGBA16Float:
                size =  8;
                return true;
            case PixelFormat.RGBA32Float:
            case PixelFormat.RGBA32Uint:
            case PixelFormat.RGBA32Sint:
                size =  16;
                return true;
            case PixelFormat.Stencil8:
                size = 1;
                return true;
            case PixelFormat.Depth16Unorm:
                size = 2;
                return true;
            case PixelFormat.Depth24Plus:
                size = 3;
                return true;
            case PixelFormat.Depth24PlusStencil8:
                size = 4;
                return true;
            case PixelFormat.Depth32Float:
                size = 4;
                return true;
            case PixelFormat.Depth32FloatStencil8:
                size = 5;
                return true;
            default:
                size = 0;
                return false;
        }
    }

    /// <summary>
    /// Try get block size in bytes for compressed formats
    /// </summary>
    /// <param name="format">The pixel format to check</param>
    /// <param name="blockSize">The size of a compressed block in bytes</param>
    /// <returns>True if the format is a compressed format and block size was retrieved, false otherwise</returns>
    public static bool TryGetCompressedBlockSize(PixelFormat format, out uint blockSize)
    {
        blockSize = format switch
        {
            // BC1 and BC4 formats use 8 bytes per 4x4 block
            PixelFormat.BC1RGBAUnorm or
            PixelFormat.BC1RGBAUnormSrgb or
            PixelFormat.BC4RUnorm or
            PixelFormat.BC4RSnorm => 8,

            // BC2, BC3, BC5, BC6H and BC7 formats use 16 bytes per 4x4 block
            PixelFormat.BC2RGBAUnorm or
            PixelFormat.BC2RGBAUnormSrgb or
            PixelFormat.BC3RGBAUnorm or
            PixelFormat.BC3RGBAUnormSrgb or
            PixelFormat.BC5RGUnorm or
            PixelFormat.BC5RGSnorm or
            PixelFormat.BC6HRGBUfloat or
            PixelFormat.BC6HRGBFloat or
            PixelFormat.BC7RGBAUnorm or
            PixelFormat.BC7RGBAUnormSrgb => 16,

            _ => 0
        };

        return blockSize != 0;
    }

}
