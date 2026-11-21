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
        DrawSprite(null, matrix, Rect.One, color, premultiplied: false, nearestFilter: false, material);
    }

    /// <summary>Draws a textured sprite covering <see cref="Rect.One"/>.</summary>
    /// <param name="texture">Texture to draw, or null for solid white.</param>
    /// <param name="matrix">Model transformation matrix.</param>
    /// <param name="color">Color tint in straight RGBA.</param>
    /// <param name="premultiplied">True when <paramref name="texture"/> already stores premultiplied alpha.</param>
    /// <param name="nearestFilter">True to sample using point/nearest filtering.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(
        Texture2D? texture,
        Matrix4x4 matrix,
        ColorFloat color,
        bool premultiplied = false,
        bool nearestFilter = false,
        Material? material = null)
    {
        DrawSprite(texture, matrix, Rect.One, color, premultiplied, nearestFilter, material);
    }

    /// <summary>Draws a sub-rectangle of a sprite texture.</summary>
    /// <param name="texture">Texture to draw, or null for solid white.</param>
    /// <param name="matrix">Model transformation matrix.</param>
    /// <param name="uvRect">Normalized UV rectangle.</param>
    /// <param name="color">Color tint in straight RGBA.</param>
    /// <param name="premultiplied">True when <paramref name="texture"/> stores premultiplied alpha (e.g. SVG).</param>
    /// <param name="nearestFilter">True to sample with nearest filtering.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(
        Texture2D? texture,
        Matrix4x4 matrix,
        Rect uvRect,
        ColorFloat color,
        bool premultiplied = false,
        bool nearestFilter = false,
        Material? material = null)
    {
        Texture? nativeTex = texture?.NativeTexture;
        UIBatchRenderer.QuadKind kind = UIBatchRenderer.ResolveImageKind(premultiplied && nativeTex != null, tiled: false, nearestFilter);
        _batchRenderer.DrawQuad(matrix, nativeTex, uvRect, color, kind, material);
    }

    /// <summary>Draws a 9-slice sprite by emitting up to nine proportionally clamped quads in batch order.</summary>
    /// <param name="texture">Source sprite texture.</param>
    /// <param name="matrix">Model transformation matrix scaled to the node render size.</param>
    /// <param name="uvRect">Full sprite UV rectangle.</param>
    /// <param name="color">Color tint.</param>
    /// <param name="sliceBorders">9-slice borders in texels: (left, top, right, bottom).</param>
    /// <param name="premultiplied">True for premultiplied textures.</param>
    /// <param name="nearestFilter">True for nearest sampling.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    public void DrawNineSlice(
        Texture2D texture,
        Matrix4x4 matrix,
        Rect uvRect,
        ColorFloat color,
        Vector4 sliceBorders,
        bool premultiplied = false,
        bool nearestFilter = false,
        Material? material = null)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _batchRenderer.DrawNineSlice(matrix, texture.NativeTexture, uvRect, color, sliceBorders, premultiplied, nearestFilter, material);
    }

    /// <summary>Draws a repeating tiled sprite using linear-repeat wrap sampling.</summary>
    /// <param name="texture">Tileable source texture.</param>
    /// <param name="matrix">Model transformation matrix scaled to the node render size.</param>
    /// <param name="uvRect">Base UV sub-rectangle.</param>
    /// <param name="tilingScale">Tile repeat frequency multiplier.</param>
    /// <param name="color">Color tint.</param>
    /// <param name="premultiplied">True for premultiplied textures.</param>
    /// <param name="material">Optional custom UI surface material.</param>
    public void DrawTiledSprite(
        Texture2D texture,
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
        float texW = MathF.Max(1f, texture.Width);
        float texH = MathF.Max(1f, texture.Height);
        Rect tiledUv = new(
            uvRect.Origin.X,
            uvRect.Origin.Y,
            uvRect.Size.X * (worldW * tilingScale.X / texW),
            uvRect.Size.Y * (worldH * tilingScale.Y / texH));
        UIBatchRenderer.QuadKind kind = UIBatchRenderer.ResolveImageKind(premultiplied, tiled: true, nearestFilter: false);
        _batchRenderer.DrawQuad(matrix, texture.NativeTexture, tiledUv, color, kind, material);
    }

    /// <summary>Draws arbitrary triangulated 2D UI geometry in painter order.</summary>
    /// <param name="vertices">Triangle vertices in canvas world coordinates.</param>
    /// <param name="indices">Triangle list index buffer.</param>
    /// <param name="texture">Optional texture; null uses solid white.</param>
    /// <param name="premultiplied">True when <paramref name="texture"/> stores premultiplied alpha.</param>
    /// <param name="tiled">True to use repeat wrap sampling.</param>
    /// <param name="nearestFilter">True to use nearest filtering.</param>
    public void DrawMesh(
        ReadOnlySpan<UIMeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        Texture2D? texture = null,
        bool premultiplied = false,
        bool tiled = false,
        bool nearestFilter = false)
    {
        Texture? nativeTex = texture?.NativeTexture;
        UIBatchRenderer.QuadKind kind = UIBatchRenderer.ResolveImageKind(premultiplied && nativeTex != null, tiled, nearestFilter);
        _batchRenderer.DrawMesh(vertices, indices, nativeTex, kind);
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
