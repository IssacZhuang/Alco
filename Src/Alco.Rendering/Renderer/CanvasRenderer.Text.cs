using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Alco.Graphics;

namespace Alco.Rendering;

public sealed partial class CanvasRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TextConstant
    {
        public Matrix4x4 Model;
        public BoundingBox2D Mask;
        public Vector2 VertexOffset;
    }

    public static readonly Vector2 TrueTypePositionOffset = new Vector2(-0.5f, 0);
    private const int MaxTextInstancingCount = 1024;

    private readonly GraphicsArrayBuffer<TextData> _textBufferGPU;
    private readonly NativeBuffer<TextData> _textBufferCPU;

    private int _textInstanceIndex;

    private void SetTextPipeline()
    {
        _command.SetGraphicsPipeline(_pipelineInfoText);
        _indexCount = _command.SetMesh(_meshText);
        _command.SetGraphicsResources(_textShaderId_camera, Camera.EntryReadonly);
        _command.SetGraphicsResources(_textShaderId_textBuffer, _textBufferGPU.EntryReadWrite);
    }


    #region Draw by matrix

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawString(Font font, string str, float fontSize, Matrix4x4 matrix, Pivot align, ColorFloat color, float lineSpacing, BoundingBox2D mask)
    {
        fixed (char* p = str)
        {
            return DrawTextCore(font, p, str.Length, matrix, align, color, lineSpacing, mask);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font font, char* str, int count, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing, BoundingBox2D mask)
    {
        return DrawTextCore(font, str, count, matrix, pivot, color, lineSpacing, mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe float DrawChars(Font font, ReadOnlySpan<char> str, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing, BoundingBox2D mask)
    {
        fixed (char* p = str)
        {
            return DrawTextCore(font, p, str.Length, matrix, pivot, color, lineSpacing, mask);
        }
    }

    #endregion


    //draw by matrix
    private unsafe float DrawTextCore(Font font, char* str, int count, Matrix4x4 matrix, Pivot pivot, ColorFloat color, float lineSpacing, BoundingBox2D mask)
    {
        if (count == 0)
        {
            return 0;
        }

        SetState(RenderingState.Text);

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
            textDataPtr[i] = GetTextData(c, font.GetGlyph(c), color, lineSpacing, ref x, ref y);
        }

        Vector2 textAreaSize = new Vector2(x, y + lineSpacing);

        //Transform2D transform = new Transform2D(position, rotation, Vector2.One * fontSize);

        TextConstant constant = new TextConstant
        {
            Model = matrix,
            Mask = mask,    
            VertexOffset = textAreaSize * realPivot
        };

        while (true)
        {
            remainInstanceCount = MaxTextInstancingCount - _textInstanceIndex;
            remainChars = count - localIndex;
            drawCount = Math.Min(remainInstanceCount, remainChars);

            if (remainChars <= 0)
            {
                break;
            }

            if (remainInstanceCount <= 0)
            {
                FlushBuffer();//state will be reset to None
                BeginDraw();
                SetState(RenderingState.Text);
                continue;
            }


            uint instanceStart = (uint)_textInstanceIndex;

            for (uint i = 0; i < drawCount; i++)
            {
                _textBufferGPU[_textInstanceIndex] = textDataPtr[localIndex + i];
                _textInstanceIndex++;
            }

            localIndex += drawCount;

            _command.SetGraphicsResources(_textShaderId_font, font.Texture.EntrySample);
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
}