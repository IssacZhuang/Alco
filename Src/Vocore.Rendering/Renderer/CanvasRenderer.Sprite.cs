using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class CanvasRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SpriteConstant
    {
        public Matrix4x4 Model;
        public BoundingBox2D Mask;
        public Vector4 Color;
        public Rect UvRect;
    }

    private static readonly Rect DefaultUvRect = new Rect(0, 0, 1, 1);

    private readonly Shader _shaderSprite;
    private readonly Mesh _meshSprite;

    private readonly uint _spriteShaderId_camera;
    private readonly uint _spriteShaderId_texture;


    public void SetSpritePipeline()
    {
        _command.SetGraphicsPipeline(_shaderSprite.DefaultPipeline);
        _command.SetGraphicsResources(_spriteShaderId_camera, Camera.EntryViewProjection);
        _command.SetVertexBuffer(0, _meshSprite.VertexBuffer);
        _command.SetIndexBuffer(_meshSprite.IndexBuffer, _meshSprite.IndexFormat);
    }



    #region  Draw Quad

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawQuad(Vector2 position, Vector2 size, ColorFloat color, BoundingBox2D mask)
    {
        Transform2D transform = new Transform2D(position, Rotation2D.Identity, size);
        DrawQuad(transform.Matrix, color, mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawQuad(Vector2 position, Vector2 size, Rotation2D rotation, ColorFloat color, BoundingBox2D mask)
    {
        Transform2D transform = new Transform2D(position, rotation, size);
        DrawQuad(transform.Matrix, color, mask);
    }



    #endregion


    #region Draw by matrix

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawQuad( Matrix4x4 matrix, ColorFloat color, BoundingBox2D mask)
    {
        DrawSprite(_textWhite, matrix, color, mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(Texture2D texture, Matrix4x4 matrix, ColorFloat color, BoundingBox2D mask)
    {
        DrawSpiteCore(texture, DefaultUvRect, matrix, color, mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void DrawSprite(Texture2D texture, Matrix4x4 matrix, Rect uvRect, ColorFloat color, BoundingBox2D mask)
    {
        DrawSpiteCore(texture, uvRect, matrix, color, mask);
    }

    #endregion

    private void DrawSpiteCore(Texture2D texture, Rect uvRect, Matrix4x4 matrix, ColorFloat color, BoundingBox2D mask)
    {
        SetState(RenderingState.Sprite);

        SpriteConstant constant = new SpriteConstant
        {
            Model = matrix,
            Mask = mask,
            Color = color,
            UvRect = uvRect
        };

        _command.SetGraphicsResources(_spriteShaderId_texture, texture.EntrySample);
        _command.PushConstants(ShaderStage.Vertex | ShaderStage.Fragment, constant);
        _command.DrawIndexed(_meshSprite.IndexCount, 1, 0, 0, 0);
    }

}