namespace Alco.Graphics;

public static class UtilsPixelFormat
{
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

    public static bool HasStencil(PixelFormat format)
    {
        return format == PixelFormat.Depth24PlusStencil8 || format == PixelFormat.Depth32FloatStencil8;
    }

    /// <summary>
    /// Try get pixel size in bytes, return 
    /// </summary>
    /// <param name="format"></param>
    /// <param name="size"></param>
    /// <returns></returns>
    public static bool TryGetPixelSize(PixelFormat format, out uint size)
    {
        //get the size of the pixel in bytes, throw exception if the format is compressed
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

// namespace Alco.Graphics;

// public enum PixelFormat
// {
//     Undefined = 0,
//     // 8-bit
//     R8Unorm = 1,
//     R8Snorm = 2,
//     R8Uint = 3,
//     R8Sint = 4,
//     // 16-bit
//     R16Uint = 5,
//     R16Sint = 6,
//     R16Float = 7,
//     RG8Unorm = 8,
//     RG8Snorm = 9,
//     RG8Uint = 10,
//     RG8Sint = 11,
//     // 32-bit
//     R32Float = 12,
//     R32Uint = 13,
//     R32Sint = 14,
//     RG16Uint = 15,
//     RG16Sint = 16,
//     RG16Float = 17,
//     RGBA8Unorm = 18,
//     RGBA8UnormSrgb = 19,
//     RGBA8Snorm = 20,
//     RGBA8Uint = 21,
//     RGBA8Sint = 22,
//     BGRA8Unorm = 23,
//     BGRA8UnormSrgb = 24,
//     // Packed 32-bit
//     RGB10A2Uint = 25,
//     RGB10A2Unorm = 26,
//     RG11B10Ufloat = 27,
//     RGB9E5Ufloat = 28,
//     // 64-bit
//     RG32Float = 29,
//     RG32Uint = 30,
//     RG32Sint = 31,
//     RGBA16Uint = 32,
//     RGBA16Sint = 33,
//     RGBA16Float = 34,
//     // 128-bit
//     RGBA32Float = 35,
//     RGBA32Uint = 36,
//     RGBA32Sint = 37,
//     // Depth-stencil
//     Stencil8 = 38,
//     Depth16Unorm = 39,
//     Depth24Plus = 40,
//     Depth24PlusStencil8 = 41,
//     Depth32Float = 42,
//     Depth32FloatStencil8 = 43,
//     // BC Compressed
//     BC1RGBAUnorm = 44,
//     BC1RGBAUnormSrgb = 45,
//     BC2RGBAUnorm = 46,
//     BC2RGBAUnormSrgb = 47,
//     BC3RGBAUnorm = 48,
//     BC3RGBAUnormSrgb = 49,
//     BC4RUnorm = 50,
//     BC4RSnorm = 51,
//     BC5RGUnorm = 52,
//     BC5RGSnorm = 53,
//     BC6HRGBUfloat = 54,
//     BC6HRGBFloat = 55,
//     BC7RGBAUnorm = 56,
//     BC7RGBAUnormSrgb = 57,
//     // ETC Compressed
//     ETC2RGB8Unorm = 58,
//     ETC2RGB8UnormSrgb = 59,
//     ETC2RGB8A1Unorm = 60,
//     ETC2RGB8A1UnormSrgb = 61,
//     ETC2RGBA8Unorm = 62,
//     ETC2RGBA8UnormSrgb = 63,
//     EACR11Unorm = 64,
//     EACR11Snorm = 65,
//     EACRG11Unorm = 66,
//     EACRG11Snorm = 67,
//     // ASTC Compressed
//     ASTC4x4Unorm = 68,
//     ASTC4x4UnormSrgb = 69,
//     ASTC5x4Unorm = 70,
//     ASTC5x4UnormSrgb = 71,
//     ASTC5x5Unorm = 72,
//     ASTC5x5UnormSrgb = 73,
//     ASTC6x5Unorm = 74,
//     ASTC6x5UnormSrgb = 75,
//     ASTC6x6Unorm = 76,
//     ASTC6x6UnormSrgb = 77,
//     ASTC8x5Unorm = 78,
//     ASTC8x5UnormSrgb = 79,
//     ASTC8x6Unorm = 80,
//     ASTC8x6UnormSrgb = 81,
//     ASTC8x8Unorm = 82,
//     ASTC8x8UnormSrgb = 83,
//     ASTC10x5Unorm = 84,
//     ASTC10x5UnormSrgb = 85,
//     ASTC10x6Unorm = 86,
//     ASTC10x6UnormSrgb = 87,
//     ASTC10x8Unorm = 88,
//     ASTC10x8UnormSrgb = 89,
//     ASTC10x10Unorm = 90,
//     ASTC10x10UnormSrgb = 91,
//     ASTC12x10Unorm = 92,
//     ASTC12x10UnormSrgb = 93,
//     ASTC12x12Unorm = 94,
//     ASTC12x12UnormSrgb = 95,
// }
