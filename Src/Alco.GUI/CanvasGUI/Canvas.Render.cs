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

    /// <summary>
    /// Draws a sprite with custom vertices and indices using the specified texture, transformation matrix, UV rectangle, and color.
    /// </summary>
    /// <param name="vertices">The custom vertices to draw.</param>
    /// <param name="indices">The indices defining the triangulation of the vertices.</param>
    /// <param name="texture">The texture to apply to the sprite. If null, a white texture will be used.</param>
    /// <param name="matrix">The transformation matrix to apply to the vertices.</param>
    /// <param name="uvRect">The UV rectangle that defines the portion of the texture to use.</param>
    /// <param name="color">The color to multiply with the texture.</param>
    public void DrawSpriteWithCustomMesh(ReadOnlySpan<Vertex> vertices, ReadOnlySpan<ushort> indices, Texture2D? texture, Matrix4x4 matrix, Rect uvRect, ColorFloat color)
    {
        SpriteConstant constant = new SpriteConstant
        {
            Model = matrix,
            Color = color,
            UvRect = uvRect
        };

        // Set stencil reference and texture directly on the existing material
        _spriteMaterial.StencilReference = _mask;
        _spriteMaterial.SetTexture(_shaderId_texture, texture ?? _renderingSystem.TextureWhite);

        _dynamicMeshRenderer.DrawWithConstant(vertices, indices, _spriteMaterial, constant);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font? font, ReadOnlySpan<char> str, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing)
    {
        font ??= DefaultFont;
        _textRenderer.StencilReference = _mask;
        return _textRenderer.DrawText(font, str, matrix, pivot, color, lineSpacing);
    }

    protected void IncreaceStencil(Texture2D? texture, Matrix4x4 matrix, Rect uvRect)
    {
        SpriteConstant constant = new SpriteConstant
        {
            Model = matrix,
            Color = new ColorFloat(1, 1, 1, 0),
            UvRect = uvRect
        };

        _stencilIncreaseMaterial.StencilReference = _mask;
        _mask = (_mask + 1) % 0xFF;
        _stencilIncreaseMaterial.SetTexture(_shaderId_texture, texture ?? _renderingSystem.TextureWhite);
        _renderContext.DrawWithConstant(_renderingSystem.MeshCenteredSprite, _stencilIncreaseMaterial, constant);
    }

    protected void DecreaseMask(Texture2D? texture, Matrix4x4 matrix, Rect uvRect)
    {
        SpriteConstant constant = new SpriteConstant
        {
            Model = matrix,
            Color = new ColorFloat(1, 1, 1, 0),
            UvRect = uvRect
        };

        _stencilDecreaseMaterial.StencilReference = _mask;

        if (_mask == 0)
        {
            _mask = 0xFF;
        }
        else
        {
            _mask--;
        }
        _stencilDecreaseMaterial.SetTexture(_shaderId_texture, texture ?? _renderingSystem.TextureWhite);
        _renderContext.DrawWithConstant(_renderingSystem.MeshCenteredSprite, _stencilDecreaseMaterial, constant);
    }
}