using System.Diagnostics.CodeAnalysis;
using Alco.Rendering;
using Alco.IO;


namespace Alco.Engine;

/// <summary>
/// The loader for true type font file
/// </summary>
public class AssetLoaderFontTTF : IAssetLoader<Font>
{
    private static readonly string[] Extensions = [FileExt.FontTrueType];
    private readonly RenderingSystem _renderingSystem;

    public string Name => "AssetLoader.Font.TTF";
    public IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderFontTTF(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }
    public Font CreateAsset(string filename, ReadOnlySpan<byte> file)
    {

        using FontAtlasPacker packer = new FontAtlasPacker(8192, 8192);

        packer.Add(file, 32, new int2[]{
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

        ReadOnlySpan<byte> bitmap = packer.Bitmap;
        int width = packer.Width;
        int height = packer.Height;
        GlyphInfo[] glyphs = packer.Glyphs;

        return _renderingSystem.CreateFont(bitmap, width, height, glyphs);
    }
}