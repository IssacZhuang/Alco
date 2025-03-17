using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The high performance sprite renderer.
/// <br/> Not thread safe but each thread can have its own renderer instance for multi-thread rendering.
/// </summary> 
public unsafe sealed class SpriteRenderer : AutoDisposable
{
    private static readonly Rect DefaultUvRect = new Rect(0, 0, 1, 1);

    private readonly Mesh _mesh;
    private readonly Material _material;

    private readonly RenderingSystem _renderingSystem;
    private readonly RenderContext _renderContext;

    private readonly uint _shaderId_texture;


    /// <summary>
    /// Initializes a new instance of the <see cref="SpriteRenderer"/> class.
    /// </summary>
    /// <param name="renderingSystem">The rendering system.</param>
    /// <param name="renderContext">The render context.</param>
    /// <param name="mesh">The mesh to use for rendering sprites.</param>
    /// <param name="material">The material to use for rendering sprites.</param>
    /// <param name="name">The name of the renderer.</param>
    internal SpriteRenderer(RenderingSystem renderingSystem, RenderContext renderContext, Mesh mesh, Material material, string name)
    {
        _renderingSystem = renderingSystem;
        _renderContext = renderContext;

        _mesh = mesh;
        _material = material.CreateInstance();

        // Get resource IDs
        _shaderId_texture = _material.GetResourceId(ShaderResourceId.Texture);
    }

    #region Draw 2D

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Vector2 position, Rotation2D rotation, Vector2 scale, ColorFloat color)
    {
        Transform2D transform = new Transform2D(position, rotation, scale);
        DrawCore(texture, DefaultUvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Vector2 position, Rotation2D rotation, Vector2 scale, Rect uvRect, ColorFloat color)
    {
        Transform2D transform = new Transform2D(position, rotation, scale);
        DrawCore(texture, uvRect, transform.Matrix, color);
    }

    public void Draw(Sprite sprite, Vector2 position, Rotation2D rotation, Vector2 scale, ColorFloat color)
    {
        Transform2D transform = new Transform2D(position, rotation, scale);
        DrawCore(sprite.Texture, sprite.UvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Transform2D transform, ColorFloat color)
    {
        DrawCore(texture, DefaultUvRect, transform.Matrix, color);

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Transform2D transform, Rect uvRect, ColorFloat color)
    {
        DrawCore(texture, uvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Sprite sprite, Transform2D transform, ColorFloat color)
    {
        DrawCore(sprite.Texture, sprite.UvRect, transform.Matrix, color);
    }


    #endregion

    #region Draw 3D

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Vector3 position, Quaternion rotation, Vector3 scale, ColorFloat color)
    {
        Transform3D transform = new Transform3D(position, rotation, scale);
        DrawCore(texture, DefaultUvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Vector3 position, Quaternion rotation, Vector3 scale, Rect uvRect, ColorFloat color)
    {
        Transform3D transform = new Transform3D(position, rotation, scale);
        DrawCore(texture, uvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Sprite sprite, Vector3 position, Quaternion rotation, Vector3 scale, ColorFloat color)
    {
        Transform3D transform = new Transform3D(position, rotation, scale);
        DrawCore(sprite.Texture, sprite.UvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Transform3D transform, ColorFloat color)
    {
        DrawCore(texture, DefaultUvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Transform3D transform, Rect uvRect, ColorFloat color)
    {
        DrawCore(texture, uvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Sprite sprite, Transform3D transform, ColorFloat color)
    {
        DrawCore(sprite.Texture, sprite.UvRect, transform.Matrix, color);
    }

    #endregion

    #region Draw by matrix

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Matrix4x4 matrix, ColorFloat color)
    {
        DrawCore(texture, DefaultUvRect, matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Matrix4x4 matrix, Rect uvRect, ColorFloat color)
    {
        DrawCore(texture, uvRect, matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Sprite sprite, Matrix4x4 matrix, ColorFloat color)
    {
        DrawCore(sprite.Texture, sprite.UvRect, matrix, color);
    }

    #endregion

    private void DrawCore(Texture2D texture, Rect uvRect, Matrix4x4 matrix, ColorFloat color)
    {
        SpriteConstant constant = new SpriteConstant
        {
            Model = matrix,
            Color = color,
            UvRect = uvRect
        };

        _material.SetTexture(_shaderId_texture, texture);
        _renderContext.DrawWithConstant(_mesh, _material, constant);
    }

    protected override void Dispose(bool disposing)
    {
        
    }
}