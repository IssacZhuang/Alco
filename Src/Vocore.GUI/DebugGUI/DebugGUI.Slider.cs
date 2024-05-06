using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public static partial class DebugGUI
{
    public unsafe static void Slider(int min, int max, ref int value)
    {
        float minF = min;
        float maxF = max;
        float valueF = value;
        Slider(minF, maxF, ref valueF);
        value = (int)valueF;
    }

    public unsafe static void Slider(float min, float max, ref float value)
    {
        Slider(_style.SliderWidth, min, max, ref value);
    }

    public unsafe static void Slider(float width, float min, float max, ref float value)
    {
        CheckBegin();
        //slider bar

        float t = (value - min) / (max - min);
        //clamp
        t = math.clamp(t, 0, 1);
        value = math.lerp(min, max, t);

        Vector2 barDrawPos = ProcessPostion();
        Vector2 barSize = new Vector2(width + _style.Padding.X * 2, _style.FontSize + _style.Padding.Y * 2);
        Vector2 barOffset = new Vector2(barSize.X * 0.5f, 0);

        Vector2 barHitPos = new Vector2(barDrawPos.X, barDrawPos.Y - barSize.Y * 0.5f);
        BoundingBox2D barHitBox = new BoundingBox2D(barHitPos, barHitPos + barSize);
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
        Vector2 thumbOffset = new Vector2(thumbSize.X * 0.5f, 0);
        ColorFloat thumbColor = _style.SliderThumbColor;
        Vector2 thumbDrawPos = barDrawPos + new Vector2(t * (barSize.X - thumbSize.X), 0);
        Vector2 thumbHitPos = new Vector2(thumbDrawPos.X, -thumbDrawPos.Y);
        Vector2 halfThumbSize = thumbSize * 0.5f;
        thumbHitPos.X += halfThumbSize.X;
        BoundingBox2D thumbHitBox = new BoundingBox2D(thumbHitPos - halfThumbSize, thumbHitPos + halfThumbSize);

        if (thumbHitBox.Contains(_renderer.MousePosition))
        {
            thumbColor = _style.SliderThumbHoverColor;
        }

        if(barHitBox.Contains(_renderer.MousePosition) && _renderer.IsMousePressing)
        {
            float halfThumbSizeX = thumbSize.X * 0.5f;
            float mouseT = (_renderer.MousePosition.X - barHitPos.X - halfThumbSizeX) / (barSize.X- thumbSize.X);
            value = math.lerp(min, max, mouseT);
            value = math.clamp(value, min, max);
            thumbColor = _style.SliderThumbDragColor;
        }

        _renderer.DrawQuad(thumbDrawPos + thumbOffset, 50, thumbSize, thumbColor);

        SetNextOffset(new Vector2(barSize.X, barSize.Y + _style.Margin.W));
    }
}