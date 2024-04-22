using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public static partial class ImGui
{
    public unsafe static void Slider(float min, float max, ref float value)
    {
        Slider(_style.SliderWidth, min, max, ref value);
    }

    public unsafe static void Slider(float width, float min, float max, ref float value)
    {
        CheckBegin();
        //slider bar

        float quadDrawOffsetY = -_style.FontSize * 0.125f; // x/8
        float t = (value - min) / (max - min);
        //clamp
        t = math.clamp(t, 0, 1);
        value = math.lerp(min, max, t);

        Vector2 barDrawPos = ProcessPostion();
        Vector2 barSize = new Vector2(width + _style.Padding.X * 2, _style.FontSize + _style.Padding.Y * 2);
        Vector2 barOffset = new Vector2(barSize.X * 0.5f, quadDrawOffsetY);

        Vector2 barHitPos = new Vector2(barDrawPos.X, barDrawPos.Y - barSize.Y * 0.5f);
        BoundingBox2D barHitBox = new BoundingBox2D(barHitPos, barDrawPos + barSize);
        ColorFloat barColor = _style.SliderColor;
        barDrawPos.Y = -barDrawPos.Y;
        _renderer.DrawQuad(barDrawPos + barOffset, 50, barSize, barColor);

        //text
        Vector2 textDrawPos = barDrawPos;
        textDrawPos.X += barSize.X * 0.5f;

        value.TryFormat(_stringBuffer, out _stringBufferLength, ReadOnlySpan<char>.Empty, null);
        fixed (char* str = _stringBuffer)
        {
            _renderer.DrawText(textDrawPos, 0, _style.Font, str, _stringBufferLength, _style.FontSize, _style.TextColor, Pivot.Center);
        }

        //thumb
        Vector2 thumbSize = new Vector2(_style.SliderThumbWidth, barSize.Y);
        Vector2 thumbOffset = new Vector2(thumbSize.X * 0.5f, quadDrawOffsetY);
        ColorFloat thumbColor = _style.SliderThumbColor;
        Vector2 thumbDrawPos = barDrawPos + new Vector2(t * (barSize.X - thumbSize.X), 0);
        _renderer.DrawQuad(thumbDrawPos + thumbOffset, 50, thumbSize, thumbColor);


        _nextOffset = new Vector2(barSize.X, barSize.Y + _style.Margin.W);
    }
}