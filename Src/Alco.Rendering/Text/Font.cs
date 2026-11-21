using System.Text;
using SharpAstro.Fonts;

namespace Alco.Rendering;

/// <summary>Immutable CPU font asset backed by <see cref="SharpAstro.Fonts"/> providing typographic metrics and SDF glyph rasterization.</summary>
public sealed class Font
{
    private const int AsciiTableSize = 128;

    private readonly OpenTypeFont? _ttf;
    private readonly float _invUnitsPerEm;
    private readonly float[] _asciiAdvances;

    /// <summary>The diagnostic name of this font.</summary>
    public string Name { get; }

    /// <summary>Em-normalized distance from the baseline to the typographic ascender.</summary>
    public float Ascent { get; }

    /// <summary>Em-normalized non-negative magnitude from the baseline to the typographic descender.</summary>
    public float Descent { get; }

    /// <summary>Em-normalized line-to-line vertical spacing.</summary>
    public float LineHeight { get; }

    /// <summary>Whether this font is a stub placeholder with no backing font outlines.</summary>
    public bool IsEmpty => _ttf == null;

    private Font(
        OpenTypeFont? ttf,
        string name,
        float invUnitsPerEm,
        float ascent,
        float descent,
        float lineHeight)
    {
        _ttf = ttf;
        Name = name;
        _invUnitsPerEm = invUnitsPerEm;
        Ascent = ascent;
        Descent = descent;
        LineHeight = lineHeight;
        _asciiAdvances = BuildAsciiAdvances(ttf, invUnitsPerEm);
    }

    /// <summary>Creates a static immutable font asset from TrueType or OpenType binary data.</summary>
    /// <param name="fontBytes">The raw TTF/OTF file bytes.</param>
    /// <param name="name">Optional diagnostic name for logs and serialization.</param>
    /// <returns>The created font asset.</returns>
    public static Font Create(ReadOnlySpan<byte> fontBytes, string name = "font")
    {
        if (fontBytes.IsEmpty)
            throw new ArgumentException("Font binary data must not be empty.", nameof(fontBytes));

        OpenTypeFont ttf = OpenTypeFont.Load(fontBytes.ToArray());
        float invEm = ttf.UnitsPerEm > 0 ? 1.0f / ttf.UnitsPerEm : 1.0f / 1000.0f;

        float ascent = 0.8f;
        float descent = 0.2f;
        float lineHeight = 1.0f;
        if (ttf.Hhea is { } hhea)
        {
            ascent = hhea.Ascender * invEm;
            descent = MathF.Abs(hhea.Descender) * invEm;
            float computed = (hhea.Ascender - hhea.Descender + hhea.LineGap) * invEm;
            lineHeight = computed > 0f ? computed : MathF.Max(ascent + descent, 1.0f);
        }

        return new Font(ttf, name, invEm, ascent, descent, lineHeight);
    }

    /// <summary>Creates a lightweight placeholder font with stub metrics and no outlines.</summary>
    /// <param name="name">Optional diagnostic name.</param>
    /// <returns>A font with <c>Ascent = 0.8</c>, <c>Descent = 0.2</c>, <c>LineHeight = 1.0</c>, and <c>0.5</c> em advance.</returns>
    public static Font CreateEmpty(string name = "font_empty")
    {
        return new Font(null, name, 0f, 0.8f, 0.2f, 1.0f);
    }

    /// <summary>Resolves the font-specific glyph identifier for <paramref name="rune"/>, or zero when empty.</summary>
    /// <param name="rune">The Unicode scalar value to look up.</param>
    /// <returns>The glyph identifier.</returns>
    public uint GetGlyphId(Rune rune)
    {
        return _ttf != null ? _ttf.GetGlyphId((uint)rune.Value) : 0u;
    }

    /// <summary>Returns the em-normalized horizontal advance of <paramref name="rune"/>.</summary>
    /// <param name="rune">The Unicode scalar value to measure.</param>
    /// <returns>The advance width in em units.</returns>
    public float GetAdvance(Rune rune)
    {
        if ((uint)rune.Value < AsciiTableSize)
            return _asciiAdvances[rune.Value];
        if (_ttf == null)
            return Rune.IsControl(rune) ? 0f : 0.5f;

        return GetAdvanceForGlyph(_ttf.GetGlyphId((uint)rune.Value));
    }

    /// <summary>Returns the em-normalized horizontal advance of a UTF-16 BMP character.</summary>
    /// <param name="c">The character to measure.</param>
    /// <returns>The advance width in em units.</returns>
    public float GetAdvance(char c)
    {
        if (c < AsciiTableSize)
            return _asciiAdvances[c];

        return GetAdvance(Rune.TryCreate(c, out Rune rune) ? rune : Rune.ReplacementChar);
    }

    /// <summary>Returns the em-normalized horizontal advance of <paramref name="glyphId"/>.</summary>
    /// <param name="glyphId">The font-specific glyph identifier.</param>
    /// <returns>The advance width in em units.</returns>
    public float GetAdvanceForGlyph(uint glyphId)
    {
        if (_ttf?.Hmtx == null)
            return 0f;

        return _ttf.Hmtx.GetAdvanceWidth((ushort)glyphId) * _invUnitsPerEm;
    }

    /// <summary>Sums em-normalized horizontal advances over <paramref name="str"/>.</summary>
    /// <param name="str">The string to measure.</param>
    /// <returns>The total advance width in em units.</returns>
    public float GetNormalizedTextWidth(string str)
    {
        ArgumentNullException.ThrowIfNull(str);
        return GetNormalizedTextWidth(str.AsSpan());
    }

    /// <summary>Sums em-normalized horizontal advances over <paramref name="str"/>.</summary>
    /// <param name="str">The character span to measure.</param>
    /// <returns>The total advance width in em units.</returns>
    public float GetNormalizedTextWidth(ReadOnlySpan<char> str)
    {
        float width = 0f;
        foreach (Rune rune in str.EnumerateRunes())
            width += GetAdvance(rune);
        return width;
    }

    /// <summary>Rasterizes a single-channel SDF bitmap for <paramref name="glyphId"/>.</summary>
    /// <param name="glyphId">The glyph identifier to rasterize.</param>
    /// <param name="sdfHeight">Target pixels per em.</param>
    /// <param name="sdfSpread">Signed distance field spread radius in texels.</param>
    /// <param name="bitmap">The rasterized SDF bitmap when the glyph has visible outline geometry.</param>
    /// <returns><see langword="true"/> when a non-empty SDF bitmap was produced.</returns>
    public bool TryRasterizeSdf(uint glyphId, int sdfHeight, float sdfSpread, out GlyphRasterBitmap bitmap)
    {
        if (_ttf == null || sdfHeight < 4 || sdfSpread <= 0f)
        {
            bitmap = default;
            return false;
        }

        var sdf = _ttf.RenderSdf(glyphId, sdfHeight, sdfSpread);
        if (sdf.Width <= 0 || sdf.Height <= 0 || sdf.Alpha == null || sdf.Alpha.Length < sdf.Width * sdf.Height)
        {
            bitmap = default;
            return false;
        }

        bitmap = new GlyphRasterBitmap
        {
            Width = sdf.Width,
            Height = sdf.Height,
            Left = sdf.Left,
            Top = sdf.Top,
            Alpha = sdf.Alpha,
        };
        return true;
    }

    private static float[] BuildAsciiAdvances(OpenTypeFont? ttf, float invUnitsPerEm)
    {
        float[] table = new float[AsciiTableSize];
        for (int code = 0; code < AsciiTableSize; ++code)
        {
            if (code is '\r' or '\n')
                continue;

            if (ttf == null)
            {
                table[code] = char.IsControl((char)code) ? 0f : 0.5f;
                continue;
            }

            if (ttf.Hmtx != null)
            {
                uint glyphId = ttf.GetGlyphId((uint)code);
                table[code] = ttf.Hmtx.GetAdvanceWidth((ushort)glyphId) * invUnitsPerEm;
            }
        }
        return table;
    }
}
