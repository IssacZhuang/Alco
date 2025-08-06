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
public unsafe sealed class TextRenderer : AutoDisposable, ICommandListener
{

    [StructLayout(LayoutKind.Sequential)]
    private struct Constant
    {
        public Matrix4x4 Model;
        public Vector2 VertexOffset;
    }

    public static readonly Vector2 TrueTypePositionOffset = new Vector2(-0.5f, 0f);


    private const int MaxTextInstancingCount = 300;
    private static readonly uint GPUBufferSize = (uint)(MaxTextInstancingCount * sizeof(TextData));

    private readonly Mesh _mesh;
    private readonly Material _material;

    private NativeBuffer<TextData> _textBufferFull;
    private NativeBuffer<TextData> _textBufferPartial;
    private readonly List<GraphicsBuffer> _tmpGPUBuffers;
    private GraphicsBuffer? _textBufferGPU;

    private readonly RenderingSystem _renderingSystem;
    private readonly IRenderContext _renderContext;

    private uint _shaderId_textBuffer;
    private uint _shaderId_font;

    private int _instanceIndex;
    private bool _isDrawing;

    public uint? StencilReference
    {
        get => _material.StencilReference;
        set => _material.StencilReference = value;
    }


    internal TextRenderer(RenderingSystem renderingSystem, IRenderContext renderContext, Mesh mesh, Material material, string name)
    {
        _renderingSystem = renderingSystem;
        _renderContext = renderContext;
        
        _tmpGPUBuffers = new List<GraphicsBuffer>();

        _mesh = mesh;
        _material = material.CreateInstance();

        _textBufferFull = new NativeBuffer<TextData>(MaxTextInstancingCount);
        _textBufferPartial = new NativeBuffer<TextData>(MaxTextInstancingCount);


        //get resource ids
        _shaderId_textBuffer = _material.GetResourceId(ShaderResourceId.TextBuffer);
        _shaderId_font = _material.GetResourceId(ShaderResourceId.Font);

        _renderContext.AddListener(this);
    }


    void ICommandListener.OnCommandBegin()
    {
        if (_isDrawing)
        {
            throw new InvalidOperationException("TextRenderer.Begin() called twice without calling End()");
        }

        _isDrawing = true;
        _instanceIndex = 0;

        RequestGPUBuffer();
    }


    void ICommandListener.OnCommandEnd()
    {
        if (!_isDrawing)
        {
            throw new InvalidOperationException("TextRenderer.End() called without calling Begin()");
        }

        UpdateBufferToGPU();
        _isDrawing = false;

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

        _textBufferFull.SetSize(count);

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

            _material.SetTexture(_shaderId_font, font.Texture);
            _renderContext.DrawInstancedWithConstant(_mesh, _material, (uint)drawCount, instanceStart, constant);
        }

        return x;
    }


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TextData GetTextData(char c, GlyphInfo glyph, Vector4 color, float lineSpacing, ref float x, ref float y)
    {
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
            _material.SetBuffer(_shaderId_textBuffer, _textBufferGPU);
        }
        else
        {
            throw new InvalidOperationException("Failed to request GPU buffer of size: " + GPUBufferSize);
        }

    }

    protected override void Dispose(bool disposing)
    {
        _renderContext.RemoveListener(this);
        //dispose native resources
        _textBufferFull.Dispose();
        _textBufferPartial.Dispose();
    }


}