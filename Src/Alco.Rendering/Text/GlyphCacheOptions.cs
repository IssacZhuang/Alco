namespace Alco.Rendering;

/// <summary>Configuration for lazy multi-page SDF glyph atlas packing in <see cref="GlyphCache"/>.</summary>
/// <param name="PageSize">Width and height in texels of each square <c>R8Unorm</c> atlas page.</param>
/// <param name="MaxPages">Maximum number of atlas pages allocated before falling back to empty glyphs.</param>
/// <param name="SdfHeight">Target pixels per em used when rasterizing single-channel signed distance fields.</param>
/// <param name="SdfSpread">Signed distance field spread radius in SDF texels.</param>
/// <param name="Gutter">Transparent texel padding inserted around every packed glyph cell.</param>
public readonly record struct GlyphCacheOptions(
    int PageSize = 1024,
    int MaxPages = 32,
    int SdfHeight = 48,
    float SdfSpread = 6.0f,
    int Gutter = 1)
{
    /// <summary>Initializes a new <see cref="GlyphCacheOptions"/> with the recommended defaults.</summary>
    public GlyphCacheOptions() : this(1024, 32, 48, 6.0f, 1)
    {
    }

    /// <summary>Default lazy multi-page SDF glyph cache configuration.</summary>
    public static GlyphCacheOptions Default => new();
}
