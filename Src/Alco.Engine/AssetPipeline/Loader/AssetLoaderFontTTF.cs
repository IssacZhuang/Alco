using System.Diagnostics.CodeAnalysis;
using Alco.Rendering;
using Alco.IO;


namespace Alco.Engine;

/// <summary>
/// The loader for true type font file with SDF (Signed Distance Field) generation
/// </summary>
public class AssetLoaderFontTTF : BaseAssetLoader<Font>
{
    private static readonly string[] Extensions = [FileExt.FontTrueType];
    private readonly RenderingSystem _renderingSystem;

    public override string Name => "AssetLoader.Font.TTF";
    public override IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderFontTTF(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    public override object CreateAsset(in AssetLoadContext context)
    {
        // Generate regular atlas with padding for compute shader SDF generation
        using FontAtlasPacker packer = new FontAtlasPacker(
            width: 8192, 
            height: 8192,
            padding: 6  // Padding around glyphs for SDF conversion
        );

        // Old SDF generation (too slow, commented out)
        //using SdfFontAtlasPacker packer = new SdfFontAtlasPacker(8192, 8192, 32.0f, 4, 128);

        packer.Add(context.Data, 32, new int2[]{
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