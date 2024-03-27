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
    private readonly GPUPipeline _pipeline;
    private readonly Mesh _mesh;
    private readonly NativeBuffer<TextData> _textDataBuffer;
    private readonly GraphicsArrayBuffer<TextData> _textDataBUfferGPU;
    private readonly GraphicsBuffer<Matrix4x4> _cameraBuffer;
    private readonly GraphicsBuffer<Matrix4x4> _positionBuffer;
    private readonly GraphicsBuffer<IndexedIndirectData> _indirectBuffer;
    private readonly Font _font;

    private readonly GPUResuableRenderBuffer _command;
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

    public TextRenderer(GPUDevice device, GPUPipeline textPipeline, Font font)
    {
        _device = device;
        _cameraBuffer = new GraphicsBuffer<Matrix4x4>("camera_buffer");
        _positionBuffer = new GraphicsBuffer<Matrix4x4>("position_buffer");
        _indirectBuffer = new GraphicsBuffer<IndexedIndirectData>("indirect_buffer");
        _textDataBUfferGPU = new GraphicsArrayBuffer<TextData>(MaxTextInstancingCount, "text_buffer");
        
        _mesh = Mesh.Create(Vertices, Indices, "text_mesh");
        _pipeline = textPipeline;
        _command = _device.CreateResuableRenderBuffer();
        _threadId = Environment.CurrentManagedThreadId;
        _textDataBuffer = new NativeBuffer<TextData>(MaxTextInstancingCount);

        _font = font;
        _command.Begin(_device.SwapChainFrameBuffer);
        _command.SetGraphicsPipeline(_pipeline);
        _command.SetVertexBuffer(0, _mesh.VertexBuffer);
        _command.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);
        _command.SetGraphicsResources(0, _cameraBuffer.EntryReadonly);
        _command.SetGraphicsResources(1, font.Texture.EntrySample);
        _command.SetGraphicsResources(2, _textDataBUfferGPU.EntryReadonly);
        _command.SetGraphicsResources(3, _positionBuffer.EntryReadonly);
        _command.DrawIndexedIndirect(_indirectBuffer.Buffer, 0);
        _command.End();
        //prepare resuable buffer

    }

    public unsafe void DrawString(string str, float fontSize, Vector2 position, TextAlign align, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            DrawTextCore(p, (uint)str.Length, fontSize, position, align, color, lineSpacing);
        }
    }

    public unsafe void DrawChars(char* str, uint count, float fontSize, Vector2 position, TextAlign align, ColorFloat color, float lineSpacing = 1.0f)
    {
        DrawTextCore(str, count, fontSize, position, align, color, lineSpacing);
    }

    private unsafe void DrawTextCore(char* str, uint count, float fontSize, Vector2 position, TextAlign align, ColorFloat color, float lineSpacing = 1.0f)
    {
        if (count == 0)
        {
            return;
        }
        //normalized position

        float x = position.X * _invCanvasSize.X;
        float y = position.Y * _invCanvasSize.Y;

        float halfFontSize = fontSize * 0.5f;
        Transform2D transform = new Transform2D(position + new Vector2(halfFontSize, halfFontSize), Rotation2D.Identity, Vector2.One * fontSize);

        uint drawCall = count / MaxTextInstancingCount;
        uint drawRemain = count % MaxTextInstancingCount;

        char c;
        for (uint i = 0; i < drawCall; i++)
        {
            uint offset = i * MaxTextInstancingCount;
            for (int j = 0; j < MaxTextInstancingCount; j++)
            {
                c = str[offset + j];

                _textDataBUfferGPU[j] = GetTextData(c, _font.GetGlyph(c), position, color, lineSpacing, ref x, ref y);
            }

            DrawBuffer(_font, MaxTextInstancingCount, transform);
        }


        uint offset2 = drawCall * MaxTextInstancingCount;
        for (int j = 0; j < drawRemain; j++)
        {
            c = str[offset2 + j];

            _textDataBUfferGPU[j] = GetTextData(c, _font.GetGlyph(c), position, color, lineSpacing, ref x, ref y);
        }

        DrawBuffer(_font, drawRemain, transform);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DrawBuffer(Font font, uint drawCount, Transform2D transform)
    {
        _positionBuffer.Value = transform.Matrix;
        _indirectBuffer.Value = new IndexedIndirectData(_mesh.IndexCount, drawCount, 1, 0, 0);
        _indirectBuffer.UpdateBuffer();
        _positionBuffer.UpdateBuffer();
        _cameraBuffer.UpdateBuffer();
        _textDataBUfferGPU.UpdateBufferRanged(0, drawCount);
        _device.Submit(_command);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TextData GetTextData(char c, GlyphInfo glyph, Vector2 basePos, Vector4 color, float lineSpacing, ref float x, ref float y)
    {
        if (c == ' ')
        {
            x += 0.5f;
            return new TextData();
        }

        if (c == '\n' || c == '\r')
        {
            x = basePos.X;
            y += lineSpacing;
            return new TextData();
        }

        TextData data = new TextData
        {
            UVRect = glyph.UVRect,
            Color = color,
            Offset = new Vector2(x, y) + glyph.Offset,
            Size = glyph.Size
        };

        x += glyph.Advance;
        return data;
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
        _cameraBuffer.Dispose();
        _positionBuffer.Dispose();
        _textDataBuffer.Dispose();
        _textDataBUfferGPU.Dispose();
        _mesh.Dispose();
        _command.Dispose();
        
    }
}