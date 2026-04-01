using Alco.Rendering;
using Alco.IO;

namespace Alco.Engine;

/// <summary>
/// Lightweight Font asset loader for NoGPU mode.
/// Skips glyph rasterization and creates a Font with empty glyph data.
/// </summary>
public class AssetLoaderFontTTFNoGPU : BaseAssetLoader<Font>
{
    private static readonly string[] Extensions = [FileExt.FontTrueType];
    private readonly RenderingSystem _renderingSystem;

    public override string Name => "AssetLoader.Font.TTF.NoGPU";
    public override IReadOnlyList<string> FileExtensions => Extensions;

    public AssetLoaderFontTTFNoGPU(RenderingSystem renderingSystem)
    {
        _renderingSystem = renderingSystem;
    }

    /// <inheritdoc/>
    public override object CreateAsset(in AssetLoadContext context)
    {
        // Skip FontAtlasPacker entirely — create a 1x1 dummy font atlas
        ReadOnlySpan<byte> emptyBitmap = [0];
        return _renderingSystem.CreateFont(emptyBitmap, 1, 1, Array.Empty<GlyphInfo>(), context.Filename);
    }
}
