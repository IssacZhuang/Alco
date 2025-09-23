using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.Graphics;
using Alco.Rendering;

namespace Alco.GUI;

public static partial class DebugStats
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

        Text(_stringBuffer.AsSpan(0, _stringBufferLength));
        
    }

    /// <summary>
    /// Draw a text
    /// </summary>
    /// <param name="text">The text to display</param>
    public unsafe static void Text(string text)
    {
        Text(text, text.Length);
    }

    /// <summary>
    /// Draw a text of a value type
    /// </summary>
    /// <param name="value"> The value to display </param>
    /// <typeparam name="T"> The type of the value </typeparam>
    public unsafe static void Text<T>(T value) where T : ISpanFormattable
    {
        value.TryFormat(_stringBuffer, out _stringBufferLength, ReadOnlySpan<char>.Empty, null);
        Text(_stringBuffer.AsSpan(0, _stringBufferLength));
    }

    /// <summary>
    /// Draw text by a pointer
    /// </summary>
    /// <param name="str">The text</param>
    public unsafe static void Text(ReadOnlySpan<char> str)
    {
        CheckBegin();
        Vector2 drawPos = ProcessPostion();
        drawPos.Y = -drawPos.Y;

        float normalizedTextLength;
        normalizedTextLength = _renderer.DrawText(str, drawPos, _style.Font, _style.FontSize, _style.TextColor, Pivot.LeftCenter);

        float fontSize = _style.FontSize;
        SetNextOffset(new Vector2(normalizedTextLength * fontSize, fontSize + _style.Margin.W));
    }
}