using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

/// <summary>
/// The high performance text renderer.
/// <br/> Not thread safe but each thread can have its own renderer instance for multi-thread rendering.
/// </summary> 
[Obsolete]
public unsafe sealed class OldTextRenderer : AutoDisposable
{

    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        // the start of instance id in OpenGL is always 0, so use a custom instance start
        public Vector2 VertexOffset;
    }

    public static readonly Vector2 TrueTypePositionOffset = new Vector2(-0.5f, -0.5f);


    private const int MaxTextInstancingCount = 300;
    private static readonly uint GPUBufferSize = (uint)(MaxTextInstancingCount * sizeof(TextData));

    private readonly GPUDevice _device;
    private readonly RenderingSystem _renderingSystem;
    private readonly Shader _shader;
    private readonly Mesh _mesh;
    private uint _indexCount;
    // private readonly GraphicsArrayBuffer<TextData> _textBufferGPU;

    private readonly GPUCommandBuffer _command;

    private NativeBuffer<TextData> _textBufferFull;
    private NativeBuffer<TextData> _textBufferPartial;
    private readonly List<GraphicsBuffer> _tmpGPUBuffers;
    private GraphicsBuffer? _textBufferGPU;

    private GraphicsPipelineContext _pipelineInfo;

    private uint _shaderId_camera;
    private uint _shaderId_textBuffer;
    private uint _shaderId_font;

    private int _instanceIndex;
    private bool _isDrawing;
    private GPUFrameBuffer? _renderTarget;

    public GraphicsBuffer Camera { get; set; }

    internal OldTextRenderer(RenderingSystem renderingSystem, Mesh mesh, GraphicsBuffer camera, Shader shader)
    {
        _renderingSystem = renderingSystem;
        _device = renderingSystem.GraphicsDevice;
        //_textBufferGPU = renderingSystem.CreateGraphicsArrayBuffer<TextData>(MaxTextInstancingCount, "text_buffer");
        _tmpGPUBuffers = new List<GraphicsBuffer>();

        _mesh = mesh;
        _shader = shader;
        _command = _device.CreateCommandBuffer();

        _textBufferFull = new NativeBuffer<TextData>(MaxTextInstancingCount);
        _textBufferPartial = new NativeBuffer<TextData>(MaxTextInstancingCount);

        _pipelineInfo = _shader.GetGraphicsPipeline(
            renderingSystem.PrefferedSDRPass,
            DepthStencilState.Read,
            BlendState.AlphaBlend
            );

        //get resource ids
        _shaderId_camera = _pipelineInfo.GetResourceId(ShaderResourceId.Camera);
        _shaderId_textBuffer = _pipelineInfo.GetResourceId(ShaderResourceId.TextBuffer);
        _shaderId_font = _pipelineInfo.GetResourceId(ShaderResourceId.Font);

        Camera = camera;
    }

    /// <summary>
    ///  Begin drawing text on the target frame buffer.
    /// </summary>
    /// <param name="target">The target frame buffer to draw text on.</param>
    /// <exception cref="InvalidOperationException">TextRenderer.Begin() called twice without calling End()</exception>
    /// <exception cref="ArgumentNullException">The render target is null</exception>
    public unsafe void Begin(GPUFrameBuffer target)
    {
        if (_isDrawing)
        {
            throw new InvalidOperationException("TextRenderer.Begin() called twice without calling End()");
        }

        if (_shader.TryUpdatePipelineContext(ref _pipelineInfo, target.RenderPass))
        {
            _shaderId_camera = _pipelineInfo.GetResourceId(ShaderResourceId.Camera);
            _shaderId_textBuffer = _pipelineInfo.GetResourceId(ShaderResourceId.TextBuffer);
            _shaderId_font = _pipelineInfo.GetResourceId(ShaderResourceId.Font);
        }

        _renderTarget = target;
        _isDrawing = true;
        _instanceIndex = 0;

        _command.Begin();
        _command.SetFrameBuffer(_renderTarget!);
        _command.SetGraphicsPipeline(_pipelineInfo!);
        _indexCount = _command.SetMesh(_mesh);
        _command.SetGraphicsResources(_shaderId_camera, Camera.EntryReadonly);
        RequestGPUBuffer();
    }

    /// <summary>
    /// End drawing text and submit the command to GPU
    /// </summary>
    public void End()
    {

        if (!_isDrawing)
        {
            throw new InvalidOperationException("TextRenderer.End() called without calling Begin()");
        }

        UpdateBufferToGPU();
        _renderTarget = null;
        _isDrawing = false;

        _command.End();
        _renderingSystem.ScheduleCommandBuffer(_command);

        _textBufferGPU = null;
        for (int i = 0; i < _tmpGPUBuffers.Count; i++)
        {
            _renderingSystem.GraphicsBufferPool.TryReturnBuffer(_tmpGPUBuffers[i]);
        }
        _tmpGPUBuffers.Clear();
    }


    private unsafe void UpdateBufferToGPU()
    {
        if (_textBufferGPU == null)
        {
            throw new InvalidOperationException("GPU buffer not requested");
        }
        uint size = (uint)(_instanceIndex * sizeof(TextData));
        TextData* textDataPartialPtr = _textBufferPartial.UnsafePointer;
        _textBufferGPU.UpdateBuffer((byte*)textDataPartialPtr, size);
        _instanceIndex = 0;
    }

    #region  Draw 2d

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawString(Font font, string str, float fontSize, Vector2 position, Rotation2D rotation, Pivot align, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            return DrawTextCore(font, p, str.Length, fontSize, position, rotation, align, color, lineSpacing);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font font, char* str, int count, float fontSize, Vector2 position, Rotation2D rotation, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        return DrawTextCore(font, str, count, fontSize, position, rotation, pivot, color, lineSpacing);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font font, ReadOnlySpan<char> str, float fontSize, Vector2 position, Rotation2D rotation, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            return DrawTextCore(font, p, str.Length, fontSize, position, rotation, pivot, color, lineSpacing);
        }
    }

    #endregion 

    #region Draw 3d
    public unsafe float DrawString(Font font, string str, float fontSize, Vector3 position, Quaternion rotation, Pivot align, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            return DrawTextCore(font, p, str.Length, fontSize, position, rotation, align, color, lineSpacing);
        }
    }

    public unsafe float DrawChars(Font font, char* str, int count, float fontSize, Vector3 position, Quaternion rotation, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        return DrawTextCore(font, str, count, fontSize, position, rotation, pivot, color, lineSpacing);
    }

    public unsafe float DrawChars(Font font, ReadOnlySpan<char> str, float fontSize, Vector3 position, Quaternion rotation, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            return DrawTextCore(font, p, str.Length, fontSize, position, rotation, pivot, color, lineSpacing);
        }
    }

    #endregion

    #region Draw by matrix

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawString(Font font, string str, float fontSize, Matrix4x4 matrix, Pivot align, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            return DrawTextCore(font, p, str.Length, matrix, align, color, lineSpacing);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font font, char* str, int count, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        return DrawTextCore(font, str, count, matrix, pivot, color, lineSpacing);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font font, ReadOnlySpan<char> str, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        fixed (char* p = str)
        {
            return DrawTextCore(font, p, str.Length, matrix, pivot, color, lineSpacing);
        }
    }

    #endregion

    //draw 2d
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe float DrawTextCore(Font font, char* str, int count, float fontSize, Vector2 position, Rotation2D rotation, Pivot pivot, ColorFloat color, float lineSpacing)
    {
        Transform2D transform = new Transform2D(position, rotation, Vector2.One * fontSize);
        return DrawTextCore(font, str, count, transform.Matrix, pivot, color, lineSpacing);
    }

    //draw 3d
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private unsafe float DrawTextCore(Font font, char* str, int count, float fontSize, Vector3 position, Quaternion rotation, Pivot pivot, ColorFloat color, float lineSpacing)
    {
        Transform3D transform = new Transform3D(position, rotation, Vector3.One * fontSize);
        return DrawTextCore(font, str, count, transform.Matrix, pivot, color, lineSpacing);
    }

    //draw by matrix
    private unsafe float DrawTextCore(Font font, char* str, int count, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing)
    {
        if (count == 0)
        {
            return 0;
        }

        _textBufferFull.EnsureSize(count);

        float x = 0;
        float y = 0;

        char c;
        int localIndex = 0;
        int remainInstanceCount = 0;
        int remainChars = 0;
        int drawCount = 0;

        Vector2 realPivot = pivot.value = TrueTypePositionOffset - pivot.value;

        TextData* textDataFullPtr = _textBufferFull.UnsafePointer;
        for (int i = 0; i < count; i++)
        {
            c = str[i];
            textDataFullPtr[i] = GetTextData(c, font.GetGlyph(c), color, lineSpacing, ref x, ref y);
        }

        Vector2 textAreaSize = new Vector2(x, y + lineSpacing);

        //Transform2D transform = new Transform2D(position, rotation, Vector2.One * fontSize);

        Constant constant = new Constant
        {
            Model = matrix,
            VertexOffset = textAreaSize * realPivot
        };

        TextData* textDataPartialPtr = _textBufferPartial.UnsafePointer;

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
                UpdateBufferToGPU();
                RequestGPUBuffer();
                continue;
            }


            uint instanceStart = (uint)_instanceIndex;

            for (uint i = 0; i < drawCount; i++)
            {
                textDataPartialPtr[_instanceIndex] = textDataFullPtr[localIndex + i];
                _instanceIndex++;
            }

            localIndex += drawCount;

            _command.SetGraphicsResources(_shaderId_font, font.Texture.EntrySample);
            _command.PushConstants(ShaderStage.Vertex, 0, constant);
            _command.DrawIndexed(_indexCount, (uint)drawCount, 0, 0, instanceStart);
        }

        return x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TextData GetTextData(char c, GlyphInfo glyph, Vector4 color, float lineSpacing, ref float x, ref float y)
    {
        // if (c == ' ')
        // {
        //     x += 0.5f;
        //     return new TextData();
        // }

        // if (c == '\n' || c == '\r')
        // {
        //     // x = basePos.X;
        //     // y -= lineSpacing;
        //     return new TextData();
        // }

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

    private void RequestGPUBuffer()
    {
        if (_renderingSystem.GraphicsBufferPool.TryGetBuffer(GPUBufferSize, out var buffer))
        {
            _tmpGPUBuffers.Add(buffer);
            _textBufferGPU = buffer;
            _command.SetGraphicsResources(_shaderId_textBuffer, _textBufferGPU.EntryReadWrite);
        }
        else
        {
            throw new InvalidOperationException("Failed to request GPU buffer of size: " + GPUBufferSize);
        }

    }

    protected override void Dispose(bool disposing)
    {
        //dispose native resources
        _textBufferFull.Dispose();
        _textBufferPartial.Dispose();

        _command.Dispose();
    }
}