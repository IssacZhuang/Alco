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
    internal Font(MemoryRef<byte> bitmap, int width, int height, GlyphInfo[] glyphs)
    {
        _texture = Texture2D.CreateByFormat((uint)width, (uint)height, PixelFormat.R8Unorm, 1);
        _texture.SetPixels(bitmap.Pointer, bitmap.Length, 1);
        _glyphs = glyphs;
    }

    public GlyphInfo GetGlyph(char c)
    {
        return _glyphs[c];
    }

    protected override void Dispose(bool disposing)
    {
        _texture.Dispose();
    }
}