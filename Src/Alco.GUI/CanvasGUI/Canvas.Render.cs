using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public partial class Canvas
{
    /// <summary>Draws a solid-color quad using the batcher.</summary>
    /// <param name="matrix">Model transformation matrix mapping the centered unit quad into canvas space.</param>
    /// <param name="color">Quad color in straight RGBA.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawQuad(Matrix4x4 matrix, ColorFloat color, Material? material = null)
    {
        _batchRenderer.DrawQuad(matrix, null, Rect.One, color, UIBatchRenderer.QuadKind.StraightImage, material);
    }

    /// <summary>Draws a GPU texture covering <see cref="Rect.One"/>.</summary>
    /// <param name="texture">GPU texture to draw, or null for solid white.</param>
    /// <param name="matrix">Model transformation matrix.</param>
    /// <param name="color">Color tint in straight RGBA.</param>
    /// <param name="premultiplied">True when <paramref name="texture"/> already stores premultiplied alpha.</param>
    /// <param name="nearestFilter">True to sample using point/nearest filtering.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(
        Texture? texture,
        Matrix4x4 matrix,
        ColorFloat color,
        bool premultiplied = false,
        bool nearestFilter = false,
        Material? material = null)
    {
        DrawSprite(texture, matrix, Rect.One, color, premultiplied, nearestFilter, material);
    }

    /// <summary>Draws a sub-rectangle of a GPU texture.</summary>
    /// <param name="texture">GPU texture to draw, or null for solid white.</param>
    /// <param name="matrix">Model transformation matrix.</param>
    /// <param name="uvRect">Normalized UV rectangle.</param>
    /// <param name="color">Color tint in straight RGBA.</param>
    /// <param name="premultiplied">True when <paramref name="texture"/> stores premultiplied alpha (e.g. SVG).</param>
    /// <param name="nearestFilter">True to sample with nearest filtering.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(
        Texture? texture,
        Matrix4x4 matrix,
        Rect uvRect,
        ColorFloat color,
        bool premultiplied = false,
        bool nearestFilter = false,
        Material? material = null)
    {
        UIBatchRenderer.QuadKind kind = UIBatchRenderer.ResolveImageKind(premultiplied && texture != null, tiled: false, nearestFilter);
        _batchRenderer.DrawQuad(matrix, texture, uvRect, color, kind, material);
    }

    /// <summary>Draws a <see cref="Sprite"/> using its UV rectangle, filter mode, and alpha mode.</summary>
    /// <param name="sprite">Sprite to draw.</param>
    /// <param name="matrix">Model transformation matrix.</param>
    /// <param name="color">Color tint in straight RGBA.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(
        Sprite sprite,
        Matrix4x4 matrix,
        ColorFloat color,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        DrawSprite(sprite.Texture, matrix, sprite.UvRect, color, sprite.IsPremultiplied, sprite.FilterMode == Filter.Nearest, material);
    }

    /// <summary>Draws an <see cref="Image"/> asset using its import filter and alpha settings.</summary>
    /// <param name="image">Image asset to draw.</param>
    /// <param name="matrix">Model transformation matrix.</param>
    /// <param name="color">Color tint in straight RGBA.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(
        Image image,
        Matrix4x4 matrix,
        ColorFloat color,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        DrawSprite(image.DefaultSprite, matrix, color, material);
    }

    /// <summary>Draws a sub-rectangle of an <see cref="Image"/> asset using its import filter and alpha settings.</summary>
    /// <param name="image">Image asset to draw.</param>
    /// <param name="matrix">Model transformation matrix.</param>
    /// <param name="uvRect">Normalized UV rectangle.</param>
    /// <param name="color">Color tint in straight RGBA.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(
        Image image,
        Matrix4x4 matrix,
        Rect uvRect,
        ColorFloat color,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        DrawSprite(image.Texture, matrix, uvRect, color, image.IsPremultiplied, image.FilterMode == Filter.Nearest, material);
    }

    /// <summary>Draws a 9-slice sprite by emitting up to nine proportionally clamped quads in batch order.</summary>
    /// <param name="texture">Source GPU texture.</param>
    /// <param name="matrix">Model transformation matrix scaled to the node render size.</param>
    /// <param name="uvRect">Full sprite UV rectangle.</param>
    /// <param name="color">Color tint.</param>
    /// <param name="sliceBorders">9-slice borders in texels: (left, top, right, bottom).</param>
    /// <param name="premultiplied">True for premultiplied textures.</param>
    /// <param name="nearestFilter">True for nearest sampling.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    public void DrawNineSlice(
        Texture texture,
        Matrix4x4 matrix,
        Rect uvRect,
        ColorFloat color,
        Vector4 sliceBorders,
        bool premultiplied = false,
        bool nearestFilter = false,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _batchRenderer.DrawNineSlice(matrix, texture, uvRect, color, sliceBorders, premultiplied, nearestFilter, material);
    }

    /// <summary>Draws a 9-sliced <see cref="Sprite"/> using its configured slice padding, filter, and alpha mode.</summary>
    /// <param name="sprite">Sprite to draw.</param>
    /// <param name="matrix">Model transformation matrix scaled to the node render size.</param>
    /// <param name="color">Color tint.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    public void DrawNineSlice(
        Sprite sprite,
        Matrix4x4 matrix,
        ColorFloat color,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        Vector4 sliceBorders = new(sprite.SlicePadding.Left, sprite.SlicePadding.Top, sprite.SlicePadding.Right, sprite.SlicePadding.Bottom);
        DrawNineSlice(sprite.Texture, matrix, sprite.UvRect, color, sliceBorders, sprite.IsPremultiplied, sprite.FilterMode == Filter.Nearest, material);
    }

    /// <summary>Draws a 9-sliced <see cref="Sprite"/> with explicit border widths overriding <see cref="Sprite.SlicePadding"/>.</summary>
    /// <param name="sprite">Sprite to draw.</param>
    /// <param name="matrix">Model transformation matrix scaled to the node render size.</param>
    /// <param name="color">Color tint.</param>
    /// <param name="sliceBorders">Explicit 9-slice borders in texels: (left, top, right, bottom).</param>
    /// <param name="material">Optional custom UI surface material.</param>
    public void DrawNineSlice(
        Sprite sprite,
        Matrix4x4 matrix,
        ColorFloat color,
        Vector4 sliceBorders,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        DrawNineSlice(sprite.Texture, matrix, sprite.UvRect, color, sliceBorders, sprite.IsPremultiplied, sprite.FilterMode == Filter.Nearest, material);
    }

    /// <summary>Draws a 9-sliced <see cref="Image"/> asset using its <see cref="Image.SlicePadding"/>, filter, and alpha mode.</summary>
    /// <param name="image">Image asset to draw.</param>
    /// <param name="matrix">Model transformation matrix scaled to the node render size.</param>
    /// <param name="color">Color tint.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    public void DrawNineSlice(
        Image image,
        Matrix4x4 matrix,
        ColorFloat color,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        DrawNineSlice(image.DefaultSprite, matrix, color, material);
    }

    /// <summary>Draws a 9-sliced <see cref="Image"/> asset with explicit border widths overriding <see cref="Image.SlicePadding"/>.</summary>
    /// <param name="image">Image asset to draw.</param>
    /// <param name="matrix">Model transformation matrix scaled to the node render size.</param>
    /// <param name="color">Color tint.</param>
    /// <param name="sliceBorders">Explicit 9-slice borders in texels: (left, top, right, bottom).</param>
    /// <param name="material">Optional custom UI surface material.</param>
    public void DrawNineSlice(
        Image image,
        Matrix4x4 matrix,
        ColorFloat color,
        Vector4 sliceBorders,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        DrawNineSlice(image.DefaultSprite, matrix, color, sliceBorders, material);
    }

    /// <summary>Draws a repeating tiled sprite using linear-repeat wrap sampling.</summary>
    /// <param name="texture">Tileable source GPU texture.</param>
    /// <param name="matrix">Model transformation matrix scaled to the node render size.</param>
    /// <param name="uvRect">Base UV sub-rectangle.</param>
    /// <param name="tilingScale">Tile repeat frequency multiplier.</param>
    /// <param name="color">Color tint.</param>
    /// <param name="premultiplied">True for premultiplied textures.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    public void DrawTiledSprite(
        Texture texture,
        Matrix4x4 matrix,
        Rect uvRect,
        Vector2 tilingScale,
        ColorFloat color,
        bool premultiplied = false,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        float worldW = new Vector2(matrix.M11, matrix.M12).Length();
        float worldH = new Vector2(matrix.M21, matrix.M22).Length();
        float texW = MathF.Max(1f, texture.Extent.X);
        float texH = MathF.Max(1f, texture.Extent.Y);
        Rect tiledUv = new(
            uvRect.Origin.X,
            uvRect.Origin.Y,
            uvRect.Size.X * (worldW * tilingScale.X / texW),
            uvRect.Size.Y * (worldH * tilingScale.Y / texH));
        UIBatchRenderer.QuadKind kind = UIBatchRenderer.ResolveImageKind(premultiplied, tiled: true, nearestFilter: false);
        _batchRenderer.DrawQuad(matrix, texture, tiledUv, color, kind, material);
    }

    /// <summary>Draws a repeating tiled <see cref="Sprite"/> using its UV rectangle and alpha mode.</summary>
    /// <param name="sprite">Sprite to tile.</param>
    /// <param name="matrix">Model transformation matrix scaled to the node render size.</param>
    /// <param name="tilingScale">Tile repeat frequency multiplier.</param>
    /// <param name="color">Color tint.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    public void DrawTiledSprite(
        Sprite sprite,
        Matrix4x4 matrix,
        Vector2 tilingScale,
        ColorFloat color,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        DrawTiledSprite(sprite.Texture, matrix, sprite.UvRect, tilingScale, color, sprite.IsPremultiplied, material);
    }

    /// <summary>Draws a repeating tiled <see cref="Image"/> asset using its alpha mode.</summary>
    /// <param name="image">Image asset to tile.</param>
    /// <param name="matrix">Model transformation matrix scaled to the node render size.</param>
    /// <param name="tilingScale">Tile repeat frequency multiplier.</param>
    /// <param name="color">Color tint.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    public void DrawTiledSprite(
        Image image,
        Matrix4x4 matrix,
        Vector2 tilingScale,
        ColorFloat color,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        DrawTiledSprite(image.DefaultSprite, matrix, tilingScale, color, material);
    }

    /// <summary>Draws arbitrary triangulated 2D UI geometry in painter order.</summary>
    /// <param name="vertices">Triangle vertices in canvas world coordinates.</param>
    /// <param name="indices">Triangle list index buffer.</param>
    /// <param name="texture">Optional GPU texture; null uses solid white.</param>
    /// <param name="premultiplied">True when <paramref name="texture"/> stores premultiplied alpha.</param>
    /// <param name="tiled">True to use repeat wrap sampling.</param>
    /// <param name="nearestFilter">True to use nearest filtering.</param>
    public void DrawMesh(
        ReadOnlySpan<UIMeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        Texture? texture = null,
        bool premultiplied = false,
        bool tiled = false,
        bool nearestFilter = false)
    {
        UIBatchRenderer.QuadKind kind = UIBatchRenderer.ResolveImageKind(premultiplied && texture != null, tiled, nearestFilter);
        _batchRenderer.DrawMesh(vertices, indices, texture, kind);
    }

    /// <summary>Draws arbitrary triangulated 2D UI geometry textured with a <see cref="Sprite"/>.</summary>
    /// <param name="vertices">Triangle vertices in canvas world coordinates.</param>
    /// <param name="indices">Triangle list index buffer.</param>
    /// <param name="sprite">Sprite supplying texture, filter mode, and alpha mode.</param>
    /// <param name="tiled">True to use repeat wrap sampling.</param>
    public void DrawMesh(
        ReadOnlySpan<UIMeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        Sprite sprite,
        bool tiled = false)
    {
        ArgumentNullException.ThrowIfNull(sprite);
        DrawMesh(vertices, indices, sprite.Texture, sprite.IsPremultiplied, tiled, sprite.FilterMode == Filter.Nearest);
    }

    /// <summary>Draws arbitrary triangulated 2D UI geometry textured with an <see cref="Image"/> asset.</summary>
    /// <param name="vertices">Triangle vertices in canvas world coordinates.</param>
    /// <param name="indices">Triangle list index buffer.</param>
    /// <param name="image">Image asset supplying texture, filter mode, and alpha mode.</param>
    /// <param name="tiled">True to use repeat wrap sampling.</param>
    public void DrawMesh(
        ReadOnlySpan<UIMeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        Image image,
        bool tiled = false)
    {
        ArgumentNullException.ThrowIfNull(image);
        DrawMesh(vertices, indices, image.DefaultSprite, tiled);
    }

    /// <summary>Draws uniform-color SDF text aligned to <paramref name="pivot"/>.</summary>
    /// <param name="font">Font to draw with, or null for <see cref="Canvas.DefaultFont"/>.</param>
    /// <param name="str">Characters to draw.</param>
    /// <param name="matrix">Text baseline/em transform matrix.</param>
    /// <param name="pivot">Alignment pivot.</param>
    /// <param name="color">Text color.</param>
    /// <param name="lineSpacing">Line spacing in em units.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    /// <returns>Widest drawn line width in em units.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float DrawChars(
        Font? font,
        ReadOnlySpan<char> str,
        Matrix4x4 matrix,
        Pivot pivot,
        ColorFloat color,
        float lineSpacing = 1f,
        Material? material = null)
    {
        return _batchRenderer.DrawChars(font ?? DefaultFont, str, matrix, pivot, color, default, lineSpacing, material);
    }

    /// <summary>Draws rich-colored SDF text aligned to <paramref name="pivot"/> using per-range <paramref name="slices"/>.</summary>
    /// <param name="font">Font to draw with, or null for <see cref="Canvas.DefaultFont"/>.</param>
    /// <param name="str">Characters to draw.</param>
    /// <param name="matrix">Text baseline/em transform matrix.</param>
    /// <param name="pivot">Alignment pivot.</param>
    /// <param name="color">Fallback text color.</param>
    /// <param name="slices">Color override spans.</param>
    /// <param name="lineSpacing">Line spacing in em units.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    /// <returns>Widest drawn line width in em units.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float DrawChars(
        Font? font,
        ReadOnlySpan<char> str,
        Matrix4x4 matrix,
        Pivot pivot,
        ColorFloat color,
        ReadOnlySpan<TextSlice> slices,
        float lineSpacing = 1f,
        Material? material = null)
    {
        return _batchRenderer.DrawChars(font ?? DefaultFont, str, matrix, pivot, color, slices, lineSpacing, material);
    }
}
