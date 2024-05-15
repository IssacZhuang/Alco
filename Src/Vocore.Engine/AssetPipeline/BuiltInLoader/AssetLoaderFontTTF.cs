using System.Diagnostics.CodeAnalysis;
using Vocore.Rendering;

namespace Vocore.Engine;

/// <summary>
/// The loader for true type font file
/// </summary>
public class AssetLoaderFontTTF : BaseAssetLoader<Font, FontAtlasPacker>
{
    private static readonly string[] Extensions = new string[] { FileExt.FontTrueType };
    private readonly RenderingSystem _renderingSystem;
    
    public override string Name => "AssetLoader.Font.TTF";
    public override IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderFontTTF(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    protected override bool TryAsyncPreprocessCore(string filename, ReadOnlySpan<byte> file, [NotNullWhen(true)] out FontAtlasPacker? preprocessed)
    {
        FontAtlasPacker? packer = null;
        try
        {
            packer = new FontAtlasPacker(8192, 8192);

            packer.Add(file, 32, new int2[]{
            UtilsUnicode.RangeBasicLatin,
            // UtilsUnicode.RangeLatin1Supplement,
            // UtilsUnicode.RangeLatinExtendedA,
            // UtilsUnicode.RangeCyrillic,
            // UtilsUnicode.RangeGreek,
            // //japanese
            // UtilsUnicode.RangeHiragana,
            // UtilsUnicode.RangeKatakana,
            // //chinese
            // UtilsUnicode.RangeCjkUnifiedIdeographs,
            // UtilsUnicode.RangeCjkSymbolsAndPunctuation,
            // //korean
            // UtilsUnicode.RangeHangulSyllables,
            // UtilsUnicode.RangeHangulCompatibilityJamo,
        });

            preprocessed = packer;
            return true;
        }
        catch (Exception)
        {
            packer?.Dispose();
            preprocessed = null;
            return false;
        }
    }

    protected override bool TryCreateAssetCore(string filename, FontAtlasPacker preprocessed, [NotNullWhen(true)] out Font? asset)
    {
        try
        {
            ReadOnlySpan<byte> bitmap = preprocessed.Bitmap;
            int width = preprocessed.Width;
            int height = preprocessed.Height;
            GlyphInfo[] glyphs = preprocessed.Glyphs;

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
            preprocessed.Dispose();
        }
    }
}