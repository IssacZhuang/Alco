using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Vocore.Graphics;

namespace Vocore.Rendering;

public partial class CanvasRenderer
{
    [StructLayout(LayoutKind.Sequential)]
    private struct TextConstant
    {
        public Matrix4x4 Model;
        // the start of instance id in OpenGL is always 0, so use a custom instance start
        public Vector2 VertexOffset;
        public uint InstanceStart;
    }

    public static readonly Vector2 TrueTypePositionOffset = new Vector2(-0.5f, -0.5f);

    private const int MaxTextInstancingCount = 300;
    private readonly Shader _shaderText;
    private readonly Mesh _meshText;
    private readonly GraphicsArrayBuffer<TextData> _textBufferGPU;

    private readonly NativeBuffer<TextData> _textBufferCPU;

    private readonly uint _textShaderId_camera;
    private readonly uint _textShaderId_textBuffer;
    private readonly uint _textShaderId_font;

    private int _textInstanceIndex;

    private void SetTextPipeline()
    {
        _command.SetGraphicsPipeline(_shaderText.DefaultPipeline);
        _command.SetVertexBuffer(0, _meshText.VertexBuffer);
        _command.SetIndexBuffer(_meshText.IndexBuffer, _meshText.IndexFormat);
        _command.SetGraphicsResources(_textShaderId_camera, Camera.EntryViewProjection);
        _command.SetGraphicsResources(_textShaderId_textBuffer, _textBufferGPU.EntryReadonly);
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
            InstanceStart = 0,
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
                FlushBuffer();
                BeginDraw();
                continue;
            }


            uint instanceStart = (uint)_textInstanceIndex;
            constant.InstanceStart = instanceStart;

            for (uint i = 0; i < drawCount; i++)
            {
                _textBufferGPU[_textInstanceIndex] = textDataPtr[localIndex + i];
                _textInstanceIndex++;
            }

            localIndex += drawCount;

            _command.SetGraphicsResources(_textShaderId_font, font.Texture.EntrySample);
            _command.PushConstants(ShaderStage.Vertex, 0, constant);
            _command.DrawIndexed(_meshText.IndexCount, (uint)drawCount, 0, 0, 0);
        }

        return x;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TextData GetTextData(char c, GlyphInfo glyph, Vector4 color, float lineSpacing, ref float x, ref float y)
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
}