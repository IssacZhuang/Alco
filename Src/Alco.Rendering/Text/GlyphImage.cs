using System.Numerics;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>Em-normalized layout metrics and bindless atlas region for one resolved font glyph.</summary>
public readonly struct GlyphImage
{
    /// <summary>The atlas page texture containing this glyph, or null for whitespace/empty glyphs.</summary>
    public Texture? Page { get; init; }

    /// <summary>The bindless sampled descriptor slot index of <see cref="Page"/>, or zero when null.</summary>
    public uint TextureIndex => Page?.SampledIndex ?? 0;

    /// <summary>Normalized (u, v, width, height) rectangle inside <see cref="Page"/>.</summary>
    public Vector4 UvRect { get; init; }

    /// <summary>Em-normalized top-left corner relative to the baseline pen position (Y+ up).</summary>
    public Vector2 Offset { get; init; }

    /// <summary>Em-normalized quad dimensions (width, height).</summary>
    public Vector2 Size { get; init; }

    /// <summary>Em-normalized horizontal pen advance after drawing this glyph.</summary>
    public float Advance { get; init; }

    /// <summary>Whether the glyph texels store a signed distance field rather than binary alpha.</summary>
    public bool IsDistanceField { get; init; }

    /// <summary>Whether this glyph has a visual bitmap quad to draw.</summary>
    public bool HasVisual => Page != null && Size.X > 0f && Size.Y > 0f;
}
