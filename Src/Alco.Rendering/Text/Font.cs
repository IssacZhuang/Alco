using System.Runtime.CompilerServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// A font atlas texture with unicode to glyph mapping.
/// </summary>
public sealed unsafe class Font : AutoDisposable
{
    
    private readonly Texture2D _texture;
    private readonly GlyphInfo[] _glyphs;

    /// <summary>
    /// The atlas texture containing the glyph coverage data.
    /// </summary>
    public Texture2D Texture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture;
    }

    internal Font(Texture2D texture, GlyphInfo[] glyphs)
    {
        _texture = texture;
        _glyphs = glyphs;
    }

    /// <summary>
    /// Gets the metrics of the glyph for the given character.
    /// </summary>
    /// <param name="c">The character.</param>
    /// <returns>The glyph metrics.</returns>
    public GlyphInfo GetGlyph(char c)
    {
        return _glyphs[c];
    }

    /// <summary>
    /// Gets the width of the text in normalized font units.
    /// </summary>
    /// <param name="str">The text to measure.</param>
    /// <returns>The summed glyph advance width.</returns>
    public float GetNormalizedTextWidth(string str)
    {
        float width = 0;
        for (int i = 0; i < str.Length; i++)
        {
            width += GetGlyph(str[i]).Advance;
        }

        return width;
    }

    /// <summary>
    /// Gets the width of the text in normalized font units.
    /// </summary>
    /// <param name="str">The text to measure.</param>
    /// <returns>The summed glyph advance width.</returns>
    public float GetNormalizedTextWidth(ReadOnlySpan<char> str)
    {
        float width = 0;
        for (int i = 0; i < str.Length; i++)
        {
            width += GetGlyph(str[i]).Advance;
        }

        return width;
    }
    

    /// <summary>
    /// Gets the width of the text in normalized font units.
    /// </summary>
    /// <param name="str">The pointer to the text.</param>
    /// <param name="strLength">The number of characters to measure.</param>
    /// <returns>The summed glyph advance width.</returns>
    public float GetNormalizedTextWidth(char* str, int strLength)
    {
        float width = 0;
        for (int i = 0; i < strLength; i++)
        {
            width += GetGlyph(str[i]).Advance;
        }

        return width;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            //dispose non-private managed resources
            _texture.Dispose();
        }

    }
}