using Alco.Graphics;
using Alco.IO;

namespace Alco.Rendering;

/// <summary>
/// Asset loader for TrueType font files.
/// Baked atlases are persisted through a <see cref="FontAtlasCache"/> so repeated loads
/// of the same font skip glyph rasterization.
/// </summary>
public class AssetLoaderFontTTF : BaseAssetLoader<Font>
{
    private const int AtlasWidth = 8192;
    private const int AtlasHeight = 8192;
    private const int FontSize = 32;
    private const int GlyphPadding = 1;

    private static readonly string[] Extensions = [FileExt.FontTrueType];

    // Part of the bake inputs: any change here invalidates the font atlas cache.
    private static readonly int2[] UnicodeRanges =
    [
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
    ];

    private readonly RenderingSystem _renderingSystem;
    private readonly FontAtlasCache? _fontAtlasCache;

    /// <inheritdoc/>
    public override string Name => "AssetLoader.Font.TTF";

    /// <inheritdoc/>
    public override IReadOnlyList<string> FileExtensions => Extensions;

    /// <summary>Creates a TrueType font loader backed by an optional disk atlas cache.</summary>
    /// <param name="renderingSystem">The graphics rendering system.</param>
    /// <param name="cacheDirectory">The font atlas cache directory, or null to disable caching.</param>
    public AssetLoaderFontTTF(RenderingSystem renderingSystem, string? cacheDirectory = null)
    {
        _renderingSystem = renderingSystem ?? throw new ArgumentNullException(nameof(renderingSystem));
        _fontAtlasCache = cacheDirectory != null ? new FontAtlasCache(cacheDirectory) : null;
    }

    /// <inheritdoc/>
    public override object CreateAsset(in AssetLoadContext context)
    {
        ReadOnlySpan<byte> ttf = context.GetData();

        FontAtlasCacheEntry? cached = _fontAtlasCache?.TryLoad(ttf, FontSize, GlyphPadding, AtlasWidth, AtlasHeight, false, UnicodeRanges);
        if (cached != null)
        {
            return Font.Create(_renderingSystem, cached.Bitmap, cached.Width, cached.Height, cached.Glyphs, context.Filename);
        }

        using FontAtlasPacker packer = new(
            width: AtlasWidth,
            height: AtlasHeight,
            padding: GlyphPadding);

        packer.Add(ttf, FontSize, UnicodeRanges);

        ReadOnlySpan<byte> bitmap = packer.Bitmap;
        int width = packer.Width;
        int height = packer.Height;
        GlyphInfo[] glyphs = packer.Glyphs;

        // StoreAsync captures its own copies before returning, so disposing the packer below is safe.
        _ = _fontAtlasCache?.StoreAsync(ttf, FontSize, GlyphPadding, AtlasWidth, AtlasHeight, false, UnicodeRanges, bitmap, glyphs);

        return Font.Create(_renderingSystem, bitmap, width, height, glyphs, context.Filename);
    }
}