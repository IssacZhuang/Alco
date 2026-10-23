using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public partial class Canvas : AutoDisposable
{
    /// <summary>
    /// Draws a solid-color quad.
    /// </summary>
    /// <param name="matrix">The model transformation matrix.</param>
    /// <param name="color">The quad color.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawQuad(Matrix4x4 matrix, ColorFloat color)
    {
        DrawSprite(_renderingSystem.TextureWhite, matrix, Rect.One, color);
    }

    /// <summary>
    /// Draws a sprite using the full texture UV rectangle.
    /// </summary>
    /// <param name="texture">The texture to draw; null uses the white texture.</param>
    /// <param name="matrix">The model transformation matrix.</param>
    /// <param name="color">The color multiplied with the texture.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(Texture2D? texture, Matrix4x4 matrix, ColorFloat color)
    {
        DrawSprite(texture, matrix, Rect.One, color);
    }

    /// <summary>
    /// Draws a sub-rectangle of a sprite texture.
    /// </summary>
    /// <param name="texture">The texture to draw; null uses the white texture.</param>
    /// <param name="matrix">The model transformation matrix.</param>
    /// <param name="uvRect">The UV rectangle selecting the texture region.</param>
    /// <param name="color">The color multiplied with the texture.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(Texture2D? texture, Matrix4x4 matrix, Rect uvRect, ColorFloat color)
    {
        _renderContext.Pass.SetStencilReference(_mask);
        _spriteRenderer.Draw(texture?? _renderingSystem.TextureWhite, matrix, uvRect, color);
    }

    /// <summary>
    /// Draws a sprite whose texture stores premultiplied-alpha pixels (e.g. an SVG
    /// rasterization) with the canvas's premultiplied-blend sprite material.
    /// </summary>
    /// <param name="texture">The texture to apply. If null, a white texture will be used.</param>
    /// <param name="matrix">The transformation matrix to apply.</param>
    /// <param name="uvRect">The UV rectangle that defines the portion of the texture to use.</param>
    /// <param name="color">The color to multiply with the texture.</param>
    public void DrawSpritePremultiplied(Texture2D? texture, Matrix4x4 matrix, Rect uvRect, ColorFloat color)
    {
        SpriteConstant constant = new SpriteConstant
        {
            Model = matrix,
            Color = color,
            UvRect = uvRect
        };

        _premultipliedSpriteMaterial.SetTexture(_shaderId_texture, texture ?? _renderingSystem.TextureWhite);
        DrawMaterial(_premultipliedSpriteMaterial, in constant);
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

        _renderContext.Pass.SetStencilReference(_mask);
        _spriteMaterial.SetTexture(_shaderId_texture, texture ?? _renderingSystem.TextureWhite);

        _dynamicMeshRenderer.DrawWithConstant(vertices, indices, _spriteMaterial, constant);
    }

    /// <summary>
    /// Draws a sprite with custom vertices and indices using the specified material —
    /// the custom-material counterpart of <see cref="DrawSpriteWithCustomMesh"/>: no
    /// texture is injected, the material owns its resource bindings.
    /// </summary>
    /// <param name="vertices">The custom vertices to draw.</param>
    /// <param name="indices">The indices defining the triangulation of the vertices.</param>
    /// <param name="material">The material to draw with.</param>
    /// <param name="constant">The sprite constant data containing model matrix, color, and UV rect.</param>
    public void DrawSpriteWithCustomMaterial(ReadOnlySpan<Vertex> vertices, ReadOnlySpan<ushort> indices, GraphicsMaterial material, in SpriteConstant constant)
    {
        _renderContext.Pass.SetStencilReference(_mask);
        _dynamicMeshRenderer.DrawWithConstant(vertices, indices, material, constant);
    }

    /// <summary>
    /// Draws text with a uniform color.
    /// </summary>
    /// <param name="font">The font to render with; null uses the default font.</param>
    /// <param name="str">The characters to draw.</param>
    /// <param name="matrix">The transformation matrix.</param>
    /// <param name="pivot">The text pivot.</param>
    /// <param name="color">The text color.</param>
    /// <param name="lineSpacing">The normalized line spacing.</param>
    /// <param name="material">The material to draw with; null uses the default text material.</param>
    /// <returns>The normalized width of the drawn text.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font? font, ReadOnlySpan<char> str, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing, GraphicsMaterial? material = null)
    {
        font ??= DefaultFont;
        _renderContext.Pass.SetStencilReference(_mask);
        return _textRenderer.DrawText(font, str, matrix, pivot, color, lineSpacing, material);
    }

    /// <summary>
    /// Draws characters with a default color and per-range color slices.
    /// Characters not covered by any slice use the default color.
    /// </summary>
    /// <param name="font">The font to render with. If null, the default font is used.</param>
    /// <param name="str">The characters to draw.</param>
    /// <param name="matrix">The transformation matrix.</param>
    /// <param name="pivot">The text pivot.</param>
    /// <param name="color">The default color for characters not covered by any slice.</param>
    /// <param name="slices">The color slices. Later slices override earlier ones.</param>
    /// <param name="lineSpacing">The normalized line spacing.</param>
    /// <returns>The normalized width of the drawn text.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font? font, ReadOnlySpan<char> str, Matrix4x4 matrix, Pivot pivot, ColorFloat color, ReadOnlySpan<TextSlice> slices, float lineSpacing, GraphicsMaterial? material = null)
    {
        font ??= DefaultFont;
        _renderContext.Pass.SetStencilReference(_mask);
        return _textRenderer.DrawText(font, str, matrix, pivot, color, slices, lineSpacing, material);
    }

    protected void IncreaceStencil(Texture2D? texture, Matrix4x4 matrix, Rect uvRect)
    {
        SpriteConstant constant = new SpriteConstant
        {
            Model = matrix,
            Color = new ColorFloat(1, 1, 1, 0),
            UvRect = uvRect
        };

        _renderContext.Pass.SetStencilReference(_mask);
        _mask = (_mask + 1) % 0xFF;
        _stencilIncreaseMaterial.SetTexture(_shaderId_texture, texture ?? _renderingSystem.TextureWhite);
        _renderContext.Pass.DrawWithConstant(_renderingSystem.MeshCenteredSprite, _stencilIncreaseMaterial, constant);
    }

    protected void DecreaseMask(Texture2D? texture, Matrix4x4 matrix, Rect uvRect)
    {
        SpriteConstant constant = new SpriteConstant
        {
            Model = matrix,
            Color = new ColorFloat(1, 1, 1, 0),
            UvRect = uvRect
        };

        _renderContext.Pass.SetStencilReference(_mask);

        if (_mask == 0)
        {
            _mask = 0xFF;
        }
        else
        {
            _mask--;
        }
        _stencilDecreaseMaterial.SetTexture(_shaderId_texture, texture ?? _renderingSystem.TextureWhite);
        _renderContext.Pass.DrawWithConstant(_renderingSystem.MeshCenteredSprite, _stencilDecreaseMaterial, constant);
    }

    /// <summary>
    /// Creates a material instance prepared for canvas drawing: stencil clipping
    /// enabled (the canvas mask convention) and the canvas camera bound. Nodes
    /// drawing with a custom material own the returned instance.
    /// </summary>
    /// <param name="material">The shared material to instance.</param>
    /// <returns>The prepared material instance.</returns>
    public GraphicsMaterialInstance CreateUIMaterialInstance(GraphicsMaterial material)
    {
        GraphicsMaterialInstance instance = material.CreateInstance();
        instance.DepthStencilState = DepthStencilState.Default with
        {
            FrontFace = StencilFaceState.CompareEqual,
            BackFace = StencilFaceState.CompareEqual,
            StencilReadMask = 0xFF,
            StencilWriteMask = 0xFF,
        };
        BindCameraToMaterial(instance);
        return instance;
    }

    /// <summary>
    /// Binds the canvas camera buffer to the material.
    /// </summary>
    /// <param name="material">The material to bind the camera to.</param>
    public void BindCameraToMaterial(GraphicsMaterial material)
    {
        material.TrySetBuffer(ShaderResourceId.Camera, _camera);
    }

    /// <summary>
    /// Draws a quad using the specified material and constant data.
    /// </summary>
    /// <param name="material">The material to use for rendering.</param>
    /// <param name="constant">The sprite constant data containing model matrix, color, and UV rect.</param>
    public void DrawMaterial(GraphicsMaterial material, in SpriteConstant constant)
    {
        _renderContext.Pass.SetStencilReference(_mask);
        _renderContext.Pass.DrawWithConstant(_renderingSystem.MeshCenteredSprite, material, constant);
    }
}