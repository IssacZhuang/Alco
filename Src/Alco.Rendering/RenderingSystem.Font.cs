using Alco.Graphics;

namespace Alco.Rendering;

// font factory
public partial class RenderingSystem
{
    public Font CreateFontByFile(ReadOnlySpan<byte> fileBytes, string name = "font")
    {
        using FontAtlasPacker packer = new FontAtlasPacker(8192, 8192);
        packer.Add(fileBytes, 32, new int2[]{
            UtilsUnicode.RangeBasicLatin,
            UtilsUnicode.RangeLatin1Supplement,
            UtilsUnicode.RangeLatinExtendedA,
            UtilsUnicode.RangeCyrillic,
            UtilsUnicode.RangeGreek,
            //japanese
            UtilsUnicode.RangeHiragana,
            UtilsUnicode.RangeKatakana,
            //chinese
            UtilsUnicode.RangeCjkUnifiedIdeographs,
            UtilsUnicode.RangeCjkSymbolsAndPunctuation,
            //korean
            UtilsUnicode.RangeHangulSyllables,
            UtilsUnicode.RangeHangulCompatibilityJamo,
        });

        return CreateFont(packer.Bitmap, packer.Width, packer.Height, packer.Glyphs, name);
    }

    public unsafe Font CreateFont(ReadOnlySpan<byte> bitmap, int width, int height, GlyphInfo[] glyphs, string name = "font")
    {
        fixed (byte* bitmapPtr = bitmap)
        {
            Texture2D texture = CreateTexture2D(bitmapPtr, (uint)bitmap.Length, (uint)width, (uint)height, ImageLoadOption.Default with
            {
                Format = PixelFormat.R8Unorm,
                Name = name
            });

            return new Font(
                texture,
                glyphs
            );
        }
    }

    /// <summary>
    /// Creates a font from an existing texture and glyph information.
    /// Used for SDF fonts where the texture is generated via compute shader.
    /// </summary>
    /// <param name="texture">The font atlas texture (typically SDF)</param>
    /// <param name="glyphs">Array of glyph information</param>
    /// <returns>A new Font instance</returns>
    public Font CreateFont(Texture2D texture, GlyphInfo[] glyphs)
    {
        return new Font(texture, glyphs);
    }
}