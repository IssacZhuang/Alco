using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class SpriteRenderer : Renderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public Vector4 Color;
        public Rect UvRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
    }

    private static readonly Vertex[] Vertices =
   {
        new Vertex {Position = new Vector2(-0.5f, 0.5f), TexCoord = new Vector2(0, 0)},
        new Vertex {Position = new Vector2(0.5f, 0.5f), TexCoord = new Vector2(1, 0)},
        new Vertex {Position = new Vector2(0.5f, -0.5f), TexCoord = new Vector2(1, 1)},
        new Vertex {Position = new Vector2(-0.5f, -0.5f), TexCoord = new Vector2(0, 1)}
    };

    private static readonly ushort[] Indices = { 0, 1, 2, 0, 2, 3 };
    private static readonly Rect DefaultUvRect = new Rect(0, 0, 1, 1);

    private readonly GPUCommandBuffer _command;
    private readonly GPUDevice _device;
    private readonly Shader _shader;
    private readonly Mesh _mesh;

    private readonly uint _shaderId_camera;
    private readonly uint _shaderId_texture;

    public SpriteRenderer(ICamera camera, Shader shader) : base(camera)
    {
        _device = RendereringContext.Device;
        _command = _device.CreateCommandBuffer();
        _shader = shader;
        _mesh = Mesh.Create(Vertices, Indices);

        _shaderId_camera = _shader.GetResourceId("_camera");
        _shaderId_texture = _shader.GetResourceId("_texture");
    }

    public void Begin(GPUFrameBuffer target)
    {
        _command.Begin();
        _command.SetFrameBuffer(target);
        _command.SetGraphicsPipeline(_shader.Pipeline);
        _command.SetGraphicsResources(_shaderId_camera, Camera.ViewProjectionBuffer);
        _command.SetVertexBuffer(0, _mesh.VertexBuffer);
        _command.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);
    }

    public void End()
    {
        _command.End();
        _device.Submit(_command);
    }

    #region  Draw 3D

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
    public void Draw(Texture2D texture, Transform3D transform, ColorFloat color)
    {
        DrawCore(texture, DefaultUvRect, transform.Matrix, color);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Draw(Texture2D texture, Transform3D transform, Rect uvRect, ColorFloat color)
    {
        DrawCore(texture, uvRect, transform.Matrix, color);
    }

    #endregion

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

    #endregion

    private void DrawCore(Texture2D texture, Rect uvRect, Matrix4x4 matrix, ColorFloat color)
    {
        Constant constant = new Constant
        {
            Model = matrix,
            Color = color,
            UvRect = uvRect
        };

        _command.SetGraphicsResources(_shaderId_texture, texture.EntrySample);
        _command.PushConstants(ShaderStage.Vertex|ShaderStage.Fragment, constant);
        _command.DrawIndexed(_mesh.IndexCount, 1, 0, 0, 0);
    }

    protected override void Dispose(bool disposing)
    {
        _command.Dispose();
        _mesh.Dispose();
    }
}