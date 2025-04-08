using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public partial class Canvas : AutoDisposable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawQuad(Matrix4x4 matrix, ColorFloat color)
    {
        DrawSprite(_renderingSystem.TextureWhite, matrix, Rect.One, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(Texture2D? texture, Matrix4x4 matrix, ColorFloat color)
    {
        DrawSprite(texture, matrix, Rect.One, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(Texture2D? texture, Matrix4x4 matrix, Rect uvRect, ColorFloat color)
    {
        _spriteRenderer.StencilReference = _mask;
        _spriteRenderer.Draw(texture?? _renderingSystem.TextureWhite, matrix, uvRect, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font font, ReadOnlySpan<char> str, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing)
    {
        _textRenderer.StencilReference = _mask;
        return _textRenderer.DrawChars(font, str, matrix, pivot, color, lineSpacing);
    }

    public void DrawMask(Texture2D? texture, Matrix4x4 matrix, Rect uvRect)
    {
        SpriteConstant constant = new SpriteConstant
        {
            Model = matrix,
            Color = new ColorFloat(1, 1, 1, 0),
            UvRect = uvRect
        };

        _stencilWriteMaterial.StencilReference = _mask++;
        _stencilWriteMaterial.SetTexture(_shaderId_texture, texture ?? _renderingSystem.TextureWhite);
        _renderContext.DrawWithConstant(_renderingSystem.MeshCenteredSprite, _stencilWriteMaterial, constant);
    }
}