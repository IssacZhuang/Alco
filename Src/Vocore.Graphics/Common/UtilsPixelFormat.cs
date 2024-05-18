namespace Vocore.Graphics;

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
}