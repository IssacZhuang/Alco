using System.Diagnostics.CodeAnalysis;
using Alco.IO;
using Alco.Graphics;
using System.Numerics;


namespace Alco.Rendering;

/// <summary>
/// The loader for true type font file with SDF (Signed Distance Field) generation. <br/>
/// Baked atlases are persisted through a <see cref="FontAtlasCache"/> so repeated loads
/// of the same font skip glyph rasterization entirely.
/// </summary>
public class AssetLoaderFontTTF : BaseAssetLoader<Font>
{
    private const int AtlasWidth = 8192;
    private const int AtlasHeight = 8192;
    private const int FontSize = 32;

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
    private readonly Shader? _textSdfShader;
    private readonly bool _generateSdf;
    private readonly FontAtlasCache? _fontAtlasCache;

    public override string Name => "AssetLoader.Font.TTF";
    public override IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderFontTTF(RenderingSystem renderingSystem, Shader? textSdfShader = null, bool generateSdf = false, string? cacheDirectory = null)
    {
        _renderingSystem = renderingSystem;
        _textSdfShader = textSdfShader;
        _generateSdf = generateSdf && textSdfShader != null;
        _fontAtlasCache = cacheDirectory != null ? new FontAtlasCache(cacheDirectory) : null;
    }

    public override object CreateAsset(in AssetLoadContext context)
    {
        ReadOnlySpan<byte> ttf = context.GetData();
        int padding = _generateSdf ? 6 : 1; // Use padding for SDF, minimal for regular

        FontAtlasCacheEntry? cached = _fontAtlasCache?.TryLoad(ttf, FontSize, padding, AtlasWidth, AtlasHeight, _generateSdf, UnicodeRanges);
        if (cached != null)
        {
            return CreateFontFromAtlas(cached.Bitmap, cached.Glyphs, cached.Width, cached.Height, context.Filename);
        }

        using FontAtlasPacker packer = new FontAtlasPacker(
            width: AtlasWidth,
            height: AtlasHeight,
            padding: padding
        );

        packer.Add(ttf, FontSize, UnicodeRanges);

        ReadOnlySpan<byte> bitmap = packer.Bitmap;
        int width = packer.Width;
        int height = packer.Height;
        GlyphInfo[] glyphs = packer.Glyphs;

        // Store the raw (pre-SDF-adjustment) atlas asynchronously; StoreAsync captures
        // its own copies before returning, so disposing the packer below is safe.
        _ = _fontAtlasCache?.StoreAsync(ttf, FontSize, padding, AtlasWidth, AtlasHeight, _generateSdf, UnicodeRanges, bitmap, glyphs);

        return CreateFontFromAtlas(bitmap, glyphs, width, height, context.Filename);
    }

    private Font CreateFontFromAtlas(ReadOnlySpan<byte> bitmap, GlyphInfo[] glyphs, int width, int height, string name)
    {
        if (_generateSdf)
        {
            // Adjust GlyphInfo for SDF padding - expand UV coordinates to include padding area
            AdjustGlyphInfoForSdf(glyphs, width, height, 6, FontSize);

            var inputTexture = _renderingSystem.CreateRenderTexture(
                _renderingSystem.PreferredRTexturePass, (uint)width, (uint)height, "font_atlas_input"
            );

            var outputTexture = _renderingSystem.CreateRenderTexture(
                _renderingSystem.PreferredRTexturePass, (uint)width, (uint)height, "font_atlas_sdf_output"
            );

            unsafe
            {
                fixed (byte* dataPtr = bitmap)
                {
                    inputTexture.ColorTextures[0].SetPixels(dataPtr, (uint)bitmap.Length);
                }
            }

            var computeMaterial = _renderingSystem.CreateComputeMaterial(_textSdfShader!);
            using var textSdf = _renderingSystem.CreateTextSDF(computeMaterial, maxDistance: 6.0f);

            using var commandBuffer = _renderingSystem.GraphicsDevice.CreateCommandBuffer("sdf_generation");
            commandBuffer.Begin();
            using (var computePass = commandBuffer.BeginCompute())
            {
                textSdf.Generate(computePass, inputTexture, outputTexture);
            }
            commandBuffer.End();

            _renderingSystem.GraphicsDevice.Submit(commandBuffer);

            return _renderingSystem.CreateFont(outputTexture.ColorTextures[0], glyphs);
        }
        else
        {
            return _renderingSystem.CreateFont(bitmap, width, height, glyphs, name);
        }
    }

    /// <summary>
    /// Adjusts GlyphInfo properties to account for SDF padding, expanding UV coordinates 
    /// and adjusting size/offset so the mesh covers the full SDF region.
    /// </summary>
    /// <param name="glyphs">Array of glyph information to adjust</param>
    /// <param name="atlasWidth">Atlas texture width</param>
    /// <param name="atlasHeight">Atlas texture height</param>
    /// <param name="padding">SDF padding in pixels</param>
    /// <param name="fontSize">Original font size used for generation</param>
    private static void AdjustGlyphInfoForSdf(GlyphInfo[] glyphs, int atlasWidth, int atlasHeight, int padding, float fontSize)
    {
        float halfPadding = padding * 0.5f;

        float invWidth = 1.0f / atlasWidth;
        float invHeight = 1.0f / atlasHeight;
        float invFontSize = 1.0f / fontSize;
        float paddingUV = halfPadding * invFontSize; // Padding in font units

        for (int i = 0; i < glyphs.Length; i++)
        {
            ref GlyphInfo glyph = ref glyphs[i];
            
            // Skip empty glyphs
            if (glyph.UVRect.Z <= 0 || glyph.UVRect.W <= 0)
                continue;

            // Current UV coordinates (tight bounds from stb_truetype)
            float uvX = glyph.UVRect.X;      // Left
            float uvY = glyph.UVRect.Y;      // Top  
            float uvW = glyph.UVRect.Z;      // Width
            float uvH = glyph.UVRect.W;      // Height

            // Expand UV coordinates to include padding
            float expandedUvX = Math.Max(0, uvX - halfPadding * invWidth);
            float expandedUvY = Math.Max(0, uvY - halfPadding * invHeight);
            float expandedUvW = Math.Min(1.0f - expandedUvX, uvW + 2 * halfPadding * invWidth);
            float expandedUvH = Math.Min(1.0f - expandedUvY, uvH + 2 * halfPadding * invHeight);

            // Calculate how much we actually expanded (in case we hit atlas boundaries)
            float actualExpandX = uvX - expandedUvX;
            float actualExpandY = uvY - expandedUvY;

            // Update UV coordinates to include SDF padding
            glyph.UVRect = new Vector4(expandedUvX, expandedUvY, expandedUvW, expandedUvH);

            // Update size to account for expanded UV area
            float newWidth = expandedUvW * atlasWidth * invFontSize;
            float newHeight = expandedUvH * atlasHeight * invFontSize;
            glyph.Size = new Vector2(newWidth, newHeight);

            // Adjust offset to account for the expansion (move glyph position to compensate)
            float offsetAdjustX = actualExpandX * atlasWidth * invFontSize;
            float offsetAdjustY = actualExpandY * atlasHeight * invFontSize;
            glyph.Offset = new Vector2(glyph.Offset.X - offsetAdjustX, glyph.Offset.Y - offsetAdjustY);
        }
    }
}