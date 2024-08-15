using System.Diagnostics.CodeAnalysis;
using Vocore.Rendering;
using Vocore.IO;


namespace Vocore.Engine;

/// <summary>
/// The loader for true type font file
/// </summary>
public class AssetLoaderFontTTF : BaseAssetLoader<Font>
{
    private static readonly string[] Extensions = new string[] { FileExt.FontTrueType };
    private readonly RenderingSystem _renderingSystem;
    
    public override string Name => "AssetLoader.Font.TTF";
    public override IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderFontTTF(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }
    protected override bool TryCreateAssetCore(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out Font? asset)
    {
        FontAtlasPacker? packer = null;
        try
        {
            packer = new FontAtlasPacker(8192, 8192);

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

            asset = _renderingSystem.CreateFont(bitmap, width, height, glyphs);
            return true;
        }
        catch (Exception e)
        {
            asset = null;
            Log.Error(e);
            return false;
        }
        finally
        {
            packer?.Dispose();
        }
    }
}