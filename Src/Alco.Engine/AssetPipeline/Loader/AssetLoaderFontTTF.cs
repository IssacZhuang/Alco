using System.Diagnostics.CodeAnalysis;
using Alco.Rendering;
using Alco.IO;
using Alco.Graphics;
using System.Numerics;


namespace Alco.Engine;

/// <summary>
/// The loader for true type font file with SDF (Signed Distance Field) generation
/// </summary>
public class AssetLoaderFontTTF : BaseAssetLoader<Font>
{
    private static readonly string[] Extensions = [FileExt.FontTrueType];
    private readonly RenderingSystem _renderingSystem;
    private readonly Shader? _textSdfShader;
    private readonly bool _generateSdf;

    public override string Name => "AssetLoader.Font.TTF";
    public override IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderFontTTF(RenderingSystem renderingSystem, Shader? textSdfShader = null, bool generateSdf = false)
    {
        _renderingSystem = renderingSystem;
        _textSdfShader = textSdfShader;
        _generateSdf = generateSdf && textSdfShader != null;
    }

    public override object CreateAsset(in AssetLoadContext context)
    {
        // Step 1: Generate regular atlas (with padding if SDF is enabled)
        int padding = _generateSdf ? 6 : 1; // Use padding for SDF, minimal for regular
        using FontAtlasPacker packer = new FontAtlasPacker(
            width: 8192,
            height: 8192,
            padding: padding
        );

        packer.Add(context.Data, 32, new int2[]{
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
            });

        // Step 2: Get atlas data
        ReadOnlySpan<byte> bitmap = packer.Bitmap;
        int width = packer.Width;
        int height = packer.Height;
        GlyphInfo[] glyphs = packer.Glyphs;

        if (_generateSdf)
        {
            // Adjust GlyphInfo for SDF padding - expand UV coordinates to include padding area
            AdjustGlyphInfoForSdf(glyphs, width, height, padding, 32.0f);

            // Generate SDF using compute shader
            var inputTexture = _renderingSystem.CreateRenderTexture(
                _renderingSystem.PrefferedRTexturePass, (uint)width, (uint)height, "font_atlas_input"
            );

            var outputTexture = _renderingSystem.CreateRenderTexture(
                _renderingSystem.PrefferedRTexturePass, (uint)width, (uint)height, "font_atlas_sdf_output"
            );

            // Upload padded bitmap to input texture
            unsafe
            {
                fixed (byte* dataPtr = bitmap)
                {
                    inputTexture.ColorTextures[0].SetPixels(dataPtr, (uint)bitmap.Length);
                }
            }

            // Create TextSDF processor and generate SDF
            var computeMaterial = _renderingSystem.CreateComputeMaterial(_textSdfShader!);
            using var textSdf = _renderingSystem.CreateTextSDF(computeMaterial, maxDistance: 6.0f);

            // Generate SDF using compute shader
            using var commandBuffer = _renderingSystem.GraphicsDevice.CreateCommandBuffer("sdf_generation");
            commandBuffer.Begin();
            using (var computePass = commandBuffer.BeginCompute())
            {
                textSdf.Generate(computePass, inputTexture, outputTexture);
            }
            commandBuffer.End();

            // Submit command buffer and wait for completion
            _renderingSystem.GraphicsDevice.Submit(commandBuffer);

            // Create font using the SDF output texture
            return _renderingSystem.CreateFont(outputTexture.ColorTextures[0], glyphs);
        }
        else
        {
            // Create regular font from bitmap data
            return _renderingSystem.CreateFont(bitmap, width, height, glyphs);
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