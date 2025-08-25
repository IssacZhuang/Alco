using System.Diagnostics.CodeAnalysis;
using Alco.Rendering;
using Alco.IO;
using Alco.Graphics;


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

        // Step 2: Get atlas data
        ReadOnlySpan<byte> bitmap = packer.Bitmap;
        int width = packer.Width;
        int height = packer.Height;
        GlyphInfo[] glyphs = packer.Glyphs;

        if (_generateSdf)
        {
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
}