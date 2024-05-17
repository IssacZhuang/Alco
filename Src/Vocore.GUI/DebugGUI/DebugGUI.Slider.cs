using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public static partial class DebugGUI
{

    /// <summary>
    /// Draw a slider
    /// </summary>
    /// <param name="text">The text to display</param>
    /// <param name="value">The value of the slider</param>
    /// <param name="min">The min value</param>
    /// <param name="max">The max value</param>
    /// <returns><c>True</c> if the value has changed</returns>
    public static bool SliderWithText(string text, ref int value, int min, int max)
    {
        bool isChnaged = Slider(ref value, min, max);
        SameLine();
        Text(text);
        return isChnaged;
    }

    /// <summary>
    /// Draw a slider
    /// </summary>
    /// <param name="value">The value of the slider</param>
    /// <param name="min">The min value<param>
    /// <param name="max">The max value</param>
    /// <returns><c>True</c> if the value has changed</returns>
    public unsafe static bool Slider(ref int value, int min, int max)
    {
        float minF = min;
        float maxF = max;
        float valueF = value;
        bool isChanged = Slider(ref valueF, minF, maxF);
        value = (int)valueF;
        return isChanged;
    }

    /// <summary>
    /// Draw a slider
    /// </summary>
    /// <param name="text">The text to display</param>
    /// <param name="value">The value of the slider</param>
    /// <param name="min">The min value</param>
    /// <param name="max">The max value</param>
    /// <returns><c>True</c> if the value has changed</returns>
    public static bool SliderWithText(string text, ref float value, float min, float max)
    {
        bool isChanged = Slider(ref value, min, max);
        SameLine();
        Text(text);
        return isChanged;
    }

    /// <summary>
    /// Draw a slider
    /// </summary>
    /// <param name="value">The value of the slider</param>
    /// <param name="min">The min value<param>
    /// <param name="max">The max value</param>
    /// <returns><c>True</c> if the value has changed</returns>
    public unsafe static bool Slider(ref float value, float min, float max)
    {
        return Slider(_style.SliderWidth, ref value, min, max);
    }

    /// <summary>
    /// Draw a slider
    /// </summary>
    /// <param name="width">The width of the slider</param>
    /// <param name="value">The value of the slider</param>
    /// <param name="min">The min value<param>
    /// <param name="max">The max value</param>
    /// <returns><c>True</c> if the value has changed</returns>
    public unsafe static bool Slider(float width, ref float value, float min, float max)
    {
        CheckBegin();
        //slider bar
        bool isChanged = false;
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
        _renderer.DrawQuad(barDrawPos + barOffset, 0, barSize, barColor);

        

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
            isChanged = true;
        }
        _renderer.DrawQuad(thumbDrawPos + thumbOffset, 0, thumbSize, thumbColor);

        //text
        Vector2 textDrawPos = barDrawPos;
        textDrawPos.X += barSize.X * 0.5f;

        value.TryFormat(_stringBuffer, out _stringBufferLength, ReadOnlySpan<char>.Empty, null);
        fixed (char* str = _stringBuffer)
        {
            _renderer.DrawText(textDrawPos, 0, _style.Font, str, _stringBufferLength, _style.FontSize, _style.TextColor, Pivot.Center);
        }

        

        SetNextOffset(new Vector2(barSize.X, barSize.Y + _style.Margin.W));
        return isChanged;
    }
}