using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// A font atlas texture with unicode to glyph mapping.
/// </summary>
public unsafe class Font : AutoDisposable
{
    
    private readonly Texture2D _texture;
    private readonly GlyphInfo[] _glyphs;

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

    public GlyphInfo GetGlyph(char c)
    {
        return _glyphs[c];
    }

    public float GetNormalizedTextWidth(string str)
    {
        float width = 0;
        for (int i = 0; i < str.Length; i++)
        {
            width += GetGlyph(str[i]).Advance;
        }

        return width;
    }

    public float GetNormalizedTextWidth(Span<char> str)
    {
        float width = 0;
        for (int i = 0; i < str.Length; i++)
        {
            width += GetGlyph(str[i]).Advance;
        }

        return width;
    }

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
        _texture.Dispose();
    }
}