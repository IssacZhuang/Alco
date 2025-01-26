using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public static partial class DebugGUI
{
    /// <summary>
    /// Draw text with a format
    /// </summary>
    /// <param name="textFormat">The format string</param>
    /// <param name="arg1">The argument</param>
    /// <typeparam name="T1">The type of the argument</typeparam>
    public unsafe static void Text<T1>(string textFormat, T1 arg1) where T1 : ISpanFormattable
    {
        arg1.TryFormat(_stringBuffer, out _stringBufferLength, textFormat, null);
        fixed (char* ptr = _stringBuffer)
        {
            Text(ptr, _stringBufferLength);
        }
    }

    /// <summary>
    /// Draw a text
    /// </summary>
    /// <param name="text">The text to display</param>
    public unsafe static void Text(string text)
    {
        fixed (char* ptr = text)
        {
            Text(ptr, text.Length);
        }
    }

    /// <summary>
    /// Draw a text of a value type
    /// </summary>
    /// <param name="value"> The value to display </param>
    /// <typeparam name="T"> The type of the value </typeparam>
    public unsafe static void Text<T>(T value) where T : ISpanFormattable
    {
        value.TryFormat(_stringBuffer, out _stringBufferLength, ReadOnlySpan<char>.Empty, null);
        fixed (char* ptr = _stringBuffer)
        {
            Text(ptr, _stringBufferLength);
        }
    }

    /// <summary>
    /// Draw text by a pointer
    /// </summary>
    /// <param name="str">The pointer to the text</param>
    /// <param name="strLength">The length of the text</param>
    public unsafe static void Text(char* str, int strLength)
    {
        CheckBegin();
        Vector2 drawPos = ProcessPostion();
        drawPos.Y = -drawPos.Y;

        float normalizedTextLength;
        normalizedTextLength = _renderer.DrawText(drawPos, 0, _style.Font, str, strLength, _style.FontSize, _style.TextColor, Pivot.LeftCenter);

        float fontSize = _style.FontSize;
        SetNextOffset(new Vector2(normalizedTextLength * fontSize, fontSize + _style.Margin.W));
    }
}