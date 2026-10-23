using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// Font creation operations that build fonts and their atlas textures.
/// </summary>
public partial class RenderingSystem
{
    /// <summary>
    /// Creates a font and atlas texture from a font file containing the built-in glyph ranges.
    /// </summary>
    /// <param name="fileBytes">The font file contents.</param>
    /// <param name="name">The name of the atlas texture.</param>
    /// <returns>The created font.</returns>
    public Font CreateFontByFile(ReadOnlySpan<byte> fileBytes, string name = "font")
    {
        using FontAtlasPacker packer = new FontAtlasPacker(8192, 8192);
        packer.Add(fileBytes, 32, new int2[]{
            UnicodeUtility.RangeBasicLatin,
            UnicodeUtility.RangeLatin1Supplement,
            UnicodeUtility.RangeLatinExtendedA,
            UnicodeUtility.RangeCyrillic,
            UnicodeUtility.RangeGreek,
            //japanese
            UnicodeUtility.RangeHiragana,
            UnicodeUtility.RangeKatakana,
            //chinese
            UnicodeUtility.RangeCjkUnifiedIdeographs,
            UnicodeUtility.RangeCjkUnifiedIdeographsExtensionA,
            UnicodeUtility.RangeCjkSymbolsAndPunctuation,
            UnicodeUtility.RangeHalfwidthAndFullwidthForms, // Essential for Chinese punctuation (：；，。？！etc.)
            UnicodeUtility.RangeCjkCompatibilityForms,
            UnicodeUtility.RangeVerticalForms,
            //korean
            UnicodeUtility.RangeHangulSyllables,
            UnicodeUtility.RangeHangulCompatibilityJamo,
            //symbols
            UnicodeUtility.RangeMiscellaneousSymbols,
            UnicodeUtility.RangeGeneralPunctuation,
        });

        return CreateFont(packer.Bitmap, packer.Width, packer.Height, packer.Glyphs, name);
    }

    /// <summary>
    /// Creates a font from a single-channel glyph atlas bitmap.
    /// </summary>
    /// <param name="bitmap">The font atlas bitmap.</param>
    /// <param name="width">The atlas width in pixels.</param>
    /// <param name="height">The atlas height in pixels.</param>
    /// <param name="glyphs">The glyph information for the atlas.</param>
    /// <param name="name">The name of the atlas texture.</param>
    /// <returns>The created font.</returns>
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