using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public static partial class DebugStats
{
    public unsafe static bool Button(string str)
    {
        fixed (char* ptr = str)
        {
            return Button(ptr, str.Length, 0, true);
        }
    }

    public unsafe static bool Button(string str, float width)
    {
        fixed (char* ptr = str)
        {
            return Button(ptr, str.Length, width, false);
        }
    }

    public unsafe static bool Button(string str, int width, bool autoWidth)
    {
        fixed (char* ptr = str)
        {
            return Button(ptr, str.Length, width, autoWidth);
        }
    }


    public unsafe static bool Button(char* str, int strLength)
    {
        return Button(str, strLength, 0, true);
    }

    public unsafe static bool Button(char* str, int strLength, float width)
    {
        return Button(str, strLength, width, false);
    }

    public unsafe static bool Button(char* str, int strLength, float width, bool autoWidth)
    {
        CheckBegin();
        Vector2 drawPos = ProcessPostion();

        if (autoWidth)
        {
            width = _style.Font.GetNormalizedTextWidth(str, strLength) * _style.FontSize;
        }

        Vector2 size = new Vector2(width + _style.Padding.X * 2, _style.FontSize + _style.Padding.Y * 2);
        Vector2 bgOffset = new Vector2(size.X * 0.5f, 0);

        //hit
        Vector2 hitBoxPos = new Vector2(drawPos.X, drawPos.Y - size.Y * 0.5f - bgOffset.Y);
        BoundingBox2D hitBox = new BoundingBox2D(hitBoxPos, hitBoxPos + size);

        drawPos.Y = -drawPos.Y;

        ColorFloat color = _style.ButtonColor;

        bool inArea = false;
        if (hitBox.Contains(_renderer.MousePosition))
        {
            color = _style.ButtonHoverColor;
            inArea = true;

            if (_renderer.IsMousePressing)
            {
                color = _style.ButtonPressedColor;
            }
        }

       

        //bg
        _renderer.DrawQuad(drawPos + bgOffset, 0, size, color);

        //text
        Vector2 textPos = drawPos;
        textPos.X += size.X * 0.5f;

        _renderer.DrawText(textPos, 0, _style.Font, str, strLength, _style.FontSize, _style.TextColor, Pivot.Center);

        SetNextOffset(new Vector2(size.X, size.Y + _style.Margin.W));
        return inArea && _renderer.IsMouseClicked;
    }
}