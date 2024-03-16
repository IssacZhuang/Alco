using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public class TextRenderer : AutoDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Vertex
    {
        public Vector2 Position;
        public Vector2 TexCoord;
    }
    private static readonly Vertex[] Vertices =
    {
        new Vertex {Position = new Vector2(0, 0), TexCoord = new Vector2(0, 0)},
        new Vertex {Position = new Vector2(1, 0), TexCoord = new Vector2(1, 0)},
        new Vertex {Position = new Vector2(1, -1), TexCoord = new Vector2(1, 1)},
        new Vertex {Position = new Vector2(0, -1), TexCoord = new Vector2(0, 1)}
    };

    private static readonly ushort[] Indices = { 0, 1, 2, 0, 2, 3 };


    private const int MaxTextInstancingCount = 200;
    private readonly GPUDevice _device;
    private readonly GraphicsArrayBuffer<TextData> _textDataBuffer;
    private readonly GraphicsBuffer<Matrix4x4> _cameraBuffer;
    private readonly Mesh _mesh;
    private readonly GPUCommandBuffer _command;
    private readonly int _threadId;

    private Camera2D _camera;
    private Vector2 _invCanvasSize;//equal to inv camera size here

    public Camera2D Camera
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _camera;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set
        {
            _camera = value;
            _invCanvasSize = math.reciprocal(_camera.Size);
            _cameraBuffer.Value = _camera.ViewProjectionMatrix;
        }
    }

    public TextRenderer(GPUDevice device)
    {
        _device = device;
        _cameraBuffer = new GraphicsBuffer<Matrix4x4>("camera_buffer");
        _textDataBuffer = new GraphicsArrayBuffer<TextData>(MaxTextInstancingCount, "text_buffer");
        _mesh = Mesh.Create(Vertices, Indices, "text_mesh");
        _command = _device.CreateCommandBuffer();
        _threadId = Environment.CurrentManagedThreadId;
    }

    private unsafe void BuildCommand(Font font, char* str, uint count, float fontSize, Vector2 position, TextAlign align, ColorFloat color, float lineSpacing = 1.0f)
    {
        //normalized position
        float x = position.X * _invCanvasSize.X;
        float y = position.Y * _invCanvasSize.Y;

        Transform2D transform = new Transform2D(position, Rotation2D.Identity, Vector2.One * fontSize);

        char c;
        for (int i = 0; i < count; i++)
        {
            c = str[i];

            if (c == ' ')
            {
                x += 0.5f;
                _textDataBuffer[i] = new TextData();
                continue;
            }

            if (c == '\n' || c == '\r')
            {
                x = position.X;
                y += lineSpacing;
                _textDataBuffer[i] = new TextData();
                continue;
            }

            GlyphInfo glyph = font.GetGlyph(c);

            TextData data = new TextData
            {
                UVRect = glyph.UVRect,
                Color = color,
                Offset = new Vector2(x, y) + glyph.Offset,
                Size = glyph.Size
            };

            _textDataBuffer[i] = data;

            x += glyph.Advance;
        }

        _command.Begin();
        _command.SetFrameBuffer(_device.SwapChainFrameBuffer);
        //_command.SetGraphicsPipeline(_textPipeline);
        _command.SetVertexBuffer(0, _mesh.VertexBuffer);
        _command.SetIndexBuffer(_mesh.IndexBuffer, IndexFormat.Uint16);
        _command.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);
        _command.SetGraphicsResources(1, font.Texture.EntrySample);
        _command.SetGraphicsResources(2, _textDataBuffer.EntryReadonly);
        _command.PushConstants(ShaderStage.Vertex, transform.Matrix);
        _command.DrawIndexed((uint)Indices.Length, count, 0, 0, 0);
        _command.End();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [Conditional("DEBUG")]
    private void CheckThread()
    {

        if (_threadId != Environment.CurrentManagedThreadId)
        {
            throw new InvalidOperationException("TextRenderer can only call on main thread");
        }
    }

    protected override void Dispose(bool disposing)
    {

    }
}