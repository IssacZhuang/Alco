using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public static partial class ImGui
{
    public unsafe static void Text(string text)
    {
        fixed (char* ptr = text)
        {
            Text(ptr, text.Length);
        }
    }

    public unsafe static void Text<T>(T value) where T : ISpanFormattable
    {
        value.TryFormat(_stringBuffer, out _stringBufferLength, ReadOnlySpan<char>.Empty, null);
        fixed (char* ptr = _stringBuffer)
        {
            Text(ptr, _stringBufferLength);
        }
    }

    public unsafe static void Text(char* str, int strLength)
    {
        CheckBegin();
        Vector2 drawPos = ProcessPostion();
        drawPos.Y = -drawPos.Y;

        float normalizedTextLength;
        normalizedTextLength = _renderer.DrawText(drawPos, 0, _style.Font, str, strLength, _style.FontSize, _style.TextColor, Pivot.LeftCenter);

        float fontSize = _style.FontSize;
        _nextOffset = new Vector2(normalizedTextLength * fontSize, fontSize + _style.Margin.W);
    }
}