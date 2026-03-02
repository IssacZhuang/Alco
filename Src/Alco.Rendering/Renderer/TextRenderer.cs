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

    private NativeBuffer<TextData> _textBufferPartial;
    private NativeBuffer<Vector4> _charColorBuffer;
    private readonly List<GraphicsBuffer> _tmpGPUBuffers;
    private GraphicsBuffer? _textBufferGPU;

    private readonly RenderingSystem _renderingSystem;
    private readonly IRenderContext _renderContext;

    private uint _shaderId_textBuffer;
    private uint _shaderId_font;

    private int _instanceIndex;
    private bool _isDrawing;

    internal TextRenderer(RenderingSystem renderingSystem, IRenderContext renderContext, Mesh mesh, Material material, string name)
    {
        _renderingSystem = renderingSystem;
        _renderContext = renderContext;

        _tmpGPUBuffers = new List<GraphicsBuffer>();

        _mesh = mesh;
        _material = material.CreateInstance();

        _textBufferPartial = new NativeBuffer<TextData>(MaxTextInstancingCount);
        _charColorBuffer = new NativeBuffer<Vector4>(256); // Initial capacity, will grow as needed


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
    public unsafe float DrawText(Font font, ReadOnlySpan<char> str, float fontSize, Vector2 position, Rotation2D rotation, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        Transform2D transform = new Transform2D(position, rotation, Vector2.One * fontSize);
        ReadOnlySpan<TextSlice> slices = stackalloc TextSlice[1]{
            new TextSlice { Color = color, Start = 0, Length = str.Length }
        };
        return DrawTextCore(font, slices, str, transform.Matrix, pivot, color, lineSpacing);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawText(Font font, ReadOnlySpan<char> str, float fontSize, Vector2 position, Rotation2D rotation, Pivot pivot, ReadOnlySpan<TextSlice> slices, float lineSpacing = 1.0f)
    {
        Transform2D transform = new Transform2D(position, rotation, Vector2.One * fontSize);
        return DrawTextCore(font, slices, str, transform.Matrix, pivot, ColorFloat.White, lineSpacing);
    }

    #endregion 

    #region Draw 3d

    public unsafe float DrawText(Font font, ReadOnlySpan<char> str, float fontSize, Vector3 position, Quaternion rotation, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        Transform3D transform = new Transform3D(position, rotation, Vector3.One * fontSize);
        ReadOnlySpan<TextSlice> slices = stackalloc TextSlice[1]{
            new TextSlice { Color = color, Start = 0, Length = str.Length }
        };
        return DrawTextCore(font, slices, str, transform.Matrix, pivot, color, lineSpacing);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawText(Font font, ReadOnlySpan<char> str, float fontSize, Vector3 position, Quaternion rotation, Pivot pivot, ReadOnlySpan<TextSlice> slices, float lineSpacing = 1.0f)
    {
        Transform3D transform = new Transform3D(position, rotation, Vector3.One * fontSize);
        return DrawTextCore(font, slices, str, transform.Matrix, pivot, ColorFloat.White, lineSpacing);
    }

    #endregion

    #region Draw by matrix


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawText(Font font, ReadOnlySpan<char> str, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing = 1.0f)
    {
        ReadOnlySpan<TextSlice> slices = stackalloc TextSlice[1]{
            new TextSlice { Color = color, Start = 0, Length = str.Length }
        };
        return DrawTextCore(font, slices, str, matrix, pivot, color, lineSpacing);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawText(Font font, ReadOnlySpan<char> str, Matrix4x4 matrix, Pivot pivot, ReadOnlySpan<TextSlice> slices, float lineSpacing = 1.0f)
    {
        return DrawTextCore(font, slices, str, matrix, pivot, ColorFloat.White, lineSpacing);
    }

    #endregion

    private unsafe float DrawTextCore(Font font, ReadOnlySpan<TextSlice> slices, ReadOnlySpan<char> str, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing)
    {
        int length = str.Length;
        if (length == 0)
        {
            return 0;
        }

        // First pass: measure text extents (x,y) without writing to buffer
        float measureX = 0;
        float measureY = 0;
        for (int i = 0; i < length; i++)
        {
            char mc = str[i];
            var mg = font.GetGlyph(mc);
            measureX += mg.Advance;
        }

        // Pivot offset uses measured size
        Vector2 realPivot = pivot.value = TrueTypePositionOffset - pivot.value;
        Vector2 textAreaSize = new Vector2(measureX, measureY + lineSpacing);

        Constant constant = new Constant
        {
            Model = matrix,
            VertexOffset = textAreaSize * realPivot
        };

        // Build color array for each character
        // This avoids iterating through slices for every character
        // Ensure buffer has enough capacity
        if (_charColorBuffer.Length < length)
        {
            _charColorBuffer.SetSizeWithoutCopy(length);
        }


        Vector4* charColors = _charColorBuffer.UnsafePointer;

        // Initialize all characters with default color
        for (int i = 0; i < length; i++)
        {
            charColors[i] = color;
        }

        // Apply slices (later slices override earlier ones)
        if (slices.Length > 0)
        {
            for (int s = 0; s < slices.Length; s++)
            {
                TextSlice slice = slices[s];
                int startIdx = slice.Start;
                int sliceLen = slice.Length;
                if (sliceLen <= 0) continue;
                if (startIdx < 0) startIdx = 0;
                if (startIdx > length) startIdx = length;
                int endIdx = startIdx + sliceLen;
                if (endIdx > length) endIdx = length;
                if (startIdx >= endIdx) continue;

                // Apply this slice's color to all characters in its range
                for (int i = startIdx; i < endIdx; i++)
                {
                    charColors[i] = slice.Color;
                }
            }
        }

        // Second pass: emit glyphs directly into the staging buffer in chunks
        float x = 0;
        float y = 0;
        char c;
        int localIndex = 0;
        int remainInstanceCount = 0;
        int remainChars = 0;
        int drawCount = 0;

        TextData* textDataPartialPtr = _textBufferPartial.UnsafePointer;

        while (true)
        {
            remainInstanceCount = MaxTextInstancingCount - _instanceIndex;
            remainChars = length - localIndex;
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

            for (uint i = 0; i < (uint)drawCount; i++)
            {
                int charIndex = localIndex + (int)i;
                c = str[charIndex];

                // Use pre-computed color for this character

                textDataPartialPtr[_instanceIndex] = GetTextData(c, font.GetGlyph(c), charColors[charIndex], lineSpacing, ref x, ref y);
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
        _textBufferPartial.Dispose();
        _charColorBuffer.Dispose();
    }


}