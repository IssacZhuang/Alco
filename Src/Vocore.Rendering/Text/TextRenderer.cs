using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

/// <summary>
/// The high performance text renderer. Can only be used in the main thread.
/// </summary> 
public class TextRenderer : AutoDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        // the start of instance id in OpenGL is always 0, so use a custom instance start
        public uint InstanceStart;
        public Vector2 VertexOffset;
    }

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

    public static readonly Vector2 TrueTypePositionOffset = new Vector2(-0.5f, -0.5f);


    private const int MaxTextInstancingCount = 300;
    private readonly GPUDevice _device;
    private readonly Shader _shader;
    private readonly Mesh _mesh;
    private readonly GraphicsArrayBuffer<TextData> _textBufferGPU;
    private readonly GraphicsBuffer<Matrix4x4> _cameraBuffer;

    private readonly GPUCommandBuffer _command;
    private readonly int _threadId;

    private readonly NativeBuffer<TextData> _textBufferCPU;

    private readonly uint _shaderId_camera;
    private readonly uint _shaderId_textBuffer;
    private readonly uint _shaderId_font;

    private int _instanceIndex;
    private bool _isDrawing;
    private GPUFrameBuffer? _renderTarget;
    private Camera2D _camera;
    private Vector2 _invCanvasSize;//equal to inv camera size here

    /// <summary>
    /// The camera data for rendering text.
    /// </summary> 
    /// <value></value>
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
        _textBufferGPU = new GraphicsArrayBuffer<TextData>(MaxTextInstancingCount, "text_buffer");

        _mesh = Mesh.Create(Vertices, Indices, "text_mesh");
        _shader = shader;
        _command = _device.CreateCommandBuffer();
        _threadId = Environment.CurrentManagedThreadId;

        _textBufferCPU = new NativeBuffer<TextData>(MaxTextInstancingCount);

        //get resource ids
        _shaderId_camera = _shader.GetResourceId("_camera");
        _shaderId_textBuffer = _shader.GetResourceId("_textBuffer");
        _shaderId_font = _shader.GetResourceId("_font");
    }

    /// <summary>
    ///  Begin drawing text on the target frame buffer.
    /// </summary>
    /// <param name="target">The target frame buffer to draw text on.</param>
    /// <exception cref="InvalidOperationException">TextRenderer.Begin() called twice without calling End()</exception>
    /// <exception cref="ArgumentNullException">The render target is null</exception>
    public void Begin(GPUFrameBuffer target)
    {
        CheckThread();

        if (_isDrawing)
        {
            throw new InvalidOperationException("TextRenderer.Begin() called twice without calling End()");
        }

        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        _renderTarget = target;
        _isDrawing = true;
        _cameraBuffer.UpdateBuffer();
        BeginDraw();
        _instanceIndex = 0;
    }

    /// <summary>
    /// End drawing text and submit the command to GPU
    /// </summary>
    public void End()
    {
        CheckThread();

        if (!_isDrawing)
        {
            throw new InvalidOperationException("TextRenderer.End() called without calling Begin()");
        }

        Flush();
        _renderTarget = null;
        _isDrawing = false;
    }

    private void BeginDraw()
    {
        _command.Begin();
        _command.SetFrameBuffer(_renderTarget!);
        _command.SetGraphicsPipeline(_shader.Pipeline);
        _command.SetVertexBuffer(0, _mesh.VertexBuffer);
        _command.SetIndexBuffer(_mesh.IndexBuffer, _mesh.IndexFormat);
        _command.SetGraphicsResources(_shaderId_camera, _cameraBuffer.EntryReadonly);
        _command.SetGraphicsResources(_shaderId_textBuffer, _textBufferGPU.EntryReadonly);
    }

    private void Flush()
    {
        _textBufferGPU.UpdateBufferRanged(0, (uint)_instanceIndex);
        _command.End();
        _device.Submit(_command);
        _instanceIndex = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void DrawString(Font font, string str, float fontSize, Vector2 position, Rotation2D rotation, Pivot align, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            DrawTextCore(font, p, str.Length, fontSize, position, rotation, align, color, lineSpacing);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void DrawChars(Font font, char* str, int count, float fontSize, Vector2 position, Rotation2D rotation, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        DrawTextCore(font, str, count, fontSize, position, rotation, pivot, color, lineSpacing);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void DrawChars(Font font, ReadOnlySpan<char> str, float fontSize, Vector2 position, Rotation2D rotation, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            DrawTextCore(font, p, str.Length, fontSize, position, rotation, pivot, color, lineSpacing);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe void DrawChars(Font font, char[] str, float fontSize, Vector2 position, Rotation2D rotation, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            DrawTextCore(font, p, str.Length, fontSize, position, rotation, pivot, color, lineSpacing);
        }
    }

    private unsafe void DrawTextCore(Font font, char* str, int count, float fontSize, Vector2 position, Rotation2D rotation, Pivot pivot, ColorFloat color, float lineSpacing)
    {
        if (count == 0)
        {
            return;
        }

        _textBufferCPU.EnsureSize(count);

        float x = 0;
        float y = 0;

        char c;
        int localIndex = 0;
        int remainInstanceCount = 0;
        int remainChars = 0;
        int drawCount = 0;

        Vector2 realPivot = pivot.value = TrueTypePositionOffset - pivot.value;

        TextData* textDataPtr = _textBufferCPU.UnsafePointer;
        for (int i = 0; i < count; i++)
        {
            c = str[i];
            textDataPtr[i] = GetTextData(c, font.GetGlyph(c), position, color, lineSpacing, ref x, ref y);
        }

        Vector2 textAreaSize = new Vector2(x, y + lineSpacing);

        Transform2D transform = new Transform2D(position, rotation, Vector2.One * fontSize);

        Constant constant = new Constant
        {
            Model = transform.Matrix,
            InstanceStart = 0,
            VertexOffset = textAreaSize * realPivot
        };

        while (true)
        {
            remainInstanceCount = MaxTextInstancingCount - _instanceIndex;
            remainChars = count - localIndex;
            drawCount = Math.Min(remainInstanceCount, remainChars);

            if (remainChars <= 0)
            {
                break;
            }

            if (remainInstanceCount <= 0)
            {
                Flush();
                BeginDraw();
                continue;
            }


            uint instanceStart = (uint)_instanceIndex;
            constant.InstanceStart = instanceStart;

            for (uint i = 0; i < drawCount; i++)
            {
                _textBufferGPU[_instanceIndex] = textDataPtr[localIndex + i];
                _instanceIndex++;
            }

            localIndex += drawCount;

            _command.SetGraphicsResources(_shaderId_font, font.Texture.EntrySample);
            _command.PushConstants(ShaderStage.Vertex, 0, constant);
            _command.DrawIndexed(_mesh.IndexCount, (uint)drawCount, 0, 0, 0);
        }
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
            // x = basePos.X;
            // y -= lineSpacing;
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
        _textBufferGPU.Dispose();
        _mesh.Dispose();
        _command.Dispose();
        _textBufferCPU.Dispose();
    }
}