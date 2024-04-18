using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Rendering;

namespace Vocore.GUI;

public static class ImGui
{
    private static IImGuiRenderer _renderer = new NoImGuiRenderer();
    private static ImGuiStyle _style;

    private static readonly char[] _stringBuffer = new char[1024];
    private static int _stringBufferLength = 0;

    private static bool _isSameLine = false;
    private static bool _isBegin = false;
    private static Vector2 _currentPosition = Vector2.Zero;
    private static Vector2 _nextOffset = Vector2.Zero;

    public static void Initialize(IImGuiRenderer renderer, ImGuiStyle style)
    {
        _renderer = renderer;
        _style = style;
    }

    public static void SameLine()
    {
        _isSameLine = true;
    }

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

        if (_isSameLine)
        {
            ResetSameLine();
            _currentPosition.X += _nextOffset.X;
        }
        else
        {
            _currentPosition.X = 0;
            _currentPosition.Y += _nextOffset.Y;
        }
        
        Vector2 tmp = _currentPosition;
        tmp.Y = -tmp.Y;

        float normalizedTextLength;
        normalizedTextLength = _renderer.DrawText(tmp, _style.Font, str, strLength, _style.FontSize, _style.TextColor, Pivot.LeftTop);

        float fontSize = _style.FontSize;
        _nextOffset = new Vector2(normalizedTextLength * fontSize, fontSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ResetSameLine()
    {
        _isSameLine = false;
    }

    private static void CheckBegin()
    {
        if (!_isBegin)
        {
            _renderer.Begin();
            _isBegin = true;
        }
    }

    internal static bool CheckAndSubmit()
    {
        _currentPosition = Vector2.Zero;
        _nextOffset = Vector2.Zero;

        if (_isBegin)
        {
            _renderer.End();
            _isBegin = false;
            return true;
        }

        return false;
    }
}
