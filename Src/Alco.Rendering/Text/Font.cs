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

    /// <summary>Creates a font from a single-channel (<see cref="Format.R8Unorm"/>) glyph atlas bitmap.</summary>
    /// <param name="renderingSystem">The graphics rendering system.</param>
    /// <param name="bitmap">The single-channel font atlas bitmap.</param>
    /// <param name="width">The atlas width in pixels.</param>
    /// <param name="height">The atlas height in pixels.</param>
    /// <param name="glyphs">The glyph metrics array indexed by character code.</param>
    /// <param name="name">The diagnostic name for the atlas texture.</param>
    /// <returns>A new <see cref="Font"/> owning the created atlas texture.</returns>
    public static Font Create(
        RenderingSystem renderingSystem,
        ReadOnlySpan<byte> bitmap,
        int width,
        int height,
        GlyphInfo[] glyphs,
        string name = "font")
    {
        ArgumentNullException.ThrowIfNull(renderingSystem);
        ArgumentNullException.ThrowIfNull(glyphs);

        ImageLoadOption option = ImageLoadOption.Default with
        {
            Format = Format.R8Unorm,
            Name = name,
        };
        Texture2D texture = Texture2DUtility.CreateTexture2D(renderingSystem, bitmap, (uint)width, (uint)height, option);
        return new Font(texture, glyphs);
    }

    /// <summary>Creates a font wrapping an existing <see cref="Texture2D"/> atlas.</summary>
    /// <param name="texture">The atlas texture; ownership transfers to the font.</param>
    /// <param name="glyphs">The glyph metrics array indexed by character code.</param>
    /// <returns>A new <see cref="Font"/> owning <paramref name="texture"/>.</returns>
    public static Font Create(Texture2D texture, GlyphInfo[] glyphs)
    {
        ArgumentNullException.ThrowIfNull(texture);
        ArgumentNullException.ThrowIfNull(glyphs);
        return new Font(texture, glyphs);
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