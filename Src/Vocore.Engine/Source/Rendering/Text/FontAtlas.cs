using System.Runtime.CompilerServices;
using Vocore.Graphics;

namespace Vocore.Engine;

public unsafe class FontAtlas : IDisposable
{
    private readonly Texture2D _texture;

    public Texture2D Texture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _texture;
    }
    internal FontAtlas(MemoryRef<byte> bitmap, int width, int height, GlyphInfo[] glyphs)
    {
        _texture = Texture2D.CreateByFormat((uint)width, (uint)height, PixelFormat.R8Unorm, 1);
        _texture.SetPixels(bitmap.Pointer, bitmap.Length, 1);
    }



    public void Dispose()
    {

    }
}