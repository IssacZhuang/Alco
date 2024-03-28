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


    private const int MaxTextInstancingCount = 300;
    private readonly GPUDevice _device;
    private readonly Shader _shader;
    private readonly Mesh _mesh;
    private readonly NativeBuffer<TextData> _textDataBuffer;
    private readonly GraphicsArrayBuffer<TextData> _textDataBUfferGPU;
    private readonly GraphicsBuffer<Matrix4x4> _cameraBuffer;

    private readonly GPUCommandBuffer _command;
    private readonly int _threadId;
    
    private readonly uint _shaderId_camera;
    private readonly uint _shaderId_textBuffer;
    private readonly uint _shaderId_font;

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

    public TextRenderer(GPUDevice device, Shader shader)
    {
        _device = device;
        _cameraBuffer = new GraphicsBuffer<Matrix4x4>("camera_buffer");
        _textDataBUfferGPU = new GraphicsArrayBuffer<TextData>(MaxTextInstancingCount, "text_buffer");
        
        _mesh = Mesh.Create(Vertices, Indices, "text_mesh");
        _shader = shader;
        _command = _device.CreateCommandBuffer();
        _threadId = Environment.CurrentManagedThreadId;
        _textDataBuffer = new NativeBuffer<TextData>(MaxTextInstancingCount);



        //get resource ids
        _shaderId_camera = _shader.GetResourceId("_camera");
        _shaderId_textBuffer = _shader.GetResourceId("_textBuffer");
        _shaderId_font = _shader.GetResourceId("_font");
    }

    public unsafe void DrawString(Font font, string str, float fontSize, Vector2 position, TextAlign align, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            DrawTextCore(font, p, (uint)str.Length, fontSize, position, align, color, lineSpacing);
        }
    }

    public unsafe void DrawChars(Font font, char* str, uint count, float fontSize, Vector2 position, TextAlign align, ColorFloat color, float lineSpacing = 1.0f)
    {
        DrawTextCore(font, str, count, fontSize, position, align, color, lineSpacing);
    }

    private unsafe void DrawTextCore(Font font, char* str, uint count, float fontSize, Vector2 position, TextAlign align, ColorFloat color, float lineSpacing = 1.0f)
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

                _textDataBUfferGPU[j] = GetTextData(c, font.GetGlyph(c), position, color, lineSpacing, ref x, ref y);
            }

            DrawBuffer(font, MaxTextInstancingCount, transform);
        }


        uint offset2 = drawCall * MaxTextInstancingCount;
        for (int j = 0; j < drawRemain; j++)
        {
            c = str[offset2 + j];

            _textDataBUfferGPU[j] = GetTextData(c, font.GetGlyph(c), position, color, lineSpacing, ref x, ref y);
        }

        DrawBuffer(font, drawRemain, transform);
    }

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DrawBuffer(Font font, uint drawCount, Transform2D transform)
    {
        _cameraBuffer.UpdateBuffer();
        _textDataBUfferGPU.UpdateBufferRanged(0, drawCount);

        _command.Begin();
        _command.SetFrameBuffer(_device.SwapChainFrameBuffer);
        _command.SetGraphicsPipeline(_shader.Pipeline);
        _command.SetVertexBuffer(0, _mesh.VertexBuffer);
        _command.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);
        _command.SetGraphicsResources(_shaderId_camera, _cameraBuffer.EntryReadonly);
        _command.SetGraphicsResources(_shaderId_textBuffer, _textDataBUfferGPU.EntryReadonly);
        _command.SetGraphicsResources(_shaderId_font, font.Texture.EntrySample);
        _command.PushConstants(ShaderStage.Vertex, 0, transform.Matrix);
        _command.DrawIndexed(_mesh.IndexCount, drawCount, 0, 0, 0);
        _command.End();
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
        _textDataBuffer.Dispose();
        _textDataBUfferGPU.Dispose();
        _mesh.Dispose();
        _command.Dispose();
        
    }
}