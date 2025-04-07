using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public partial class Canvas : AutoDisposable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawQuad(Matrix4x4 matrix, ColorFloat color, BoundingBox2D mask)
    {
        DrawSprite(_renderingSystem.TextureWhite, matrix, Rect.One, color, mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(Texture2D texture, Matrix4x4 matrix, ColorFloat color, BoundingBox2D mask)
    {
        DrawSprite(texture, matrix, Rect.One, color, mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(Texture2D texture, Matrix4x4 matrix, Rect uvRect, ColorFloat color, BoundingBox2D mask)
    {
        //todo: impl mask with stencil test
        _spriteRenderer.Draw(texture, matrix, uvRect, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font font, ReadOnlySpan<char> str, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing, BoundingBox2D mask)
    {
        //todo: impl mask with stencil test
        return _textRenderer.DrawChars(font, str, matrix, pivot, color, lineSpacing);
    }

}