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

    public static Font CreateFont(MemoryRef<byte> bitmap, int width, int height, GlyphInfo[] glyphs, string name = "font")
    {
        Texture2D texture = Texture2D.CreateFromData(bitmap.Pointer, bitmap.Length, (uint)width, (uint)height, 1, ImageLoadOption.Default with {
            Format = PixelFormat.R8Unorm,
            Name = name
        });

        return new Font(
            texture,
            glyphs
        ); 
    }

    protected override void Dispose(bool disposing)
    {
        _texture.Dispose();
    }
}