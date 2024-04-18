using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
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

        ResetPosition();
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
        Vector2 pos = ProcessPostion();
        pos.Y = -pos.Y;

        float normalizedTextLength;
        normalizedTextLength = _renderer.DrawText(pos, _style.Font, str, strLength, _style.FontSize, _style.TextColor, Pivot.LeftCenter);

        float fontSize = _style.FontSize;
        _nextOffset = new Vector2(normalizedTextLength * fontSize, fontSize + _style.Margin.W);
    }

    public unsafe static void Button(char* str, int strLength, int width)
    {
        CheckBegin();
        ProcessPostion();

        Vector2 pos = ProcessPostion();
        
        Vector2 size = new Vector2(width + _style.Padding.X * 2, _style.FontSize + _style.Padding.Y * 2);
        BoundingBox2D box = new BoundingBox2D(pos, pos + size);
        pos.Y = -pos.Y;

        ColorFloat color = _style.ButtonColor;
    }

    private static Vector2 ProcessPostion()
    {
        if (_isSameLine)
        {
            ResetSameLine();
            _currentPosition.X += _nextOffset.X + _style.Margin.X + _style.Margin.Y;
        }
        else
        {
            _currentPosition.X = _style.Margin.X;
            _currentPosition.Y += _nextOffset.Y + _style.Margin.Z;
        }

        return _currentPosition;
    }

    private static void ResetPosition()
    {
        _currentPosition = new Vector2(_style.Margin.X, _style.Margin.Z + _style.FontSize * 0.5f);
        _nextOffset = Vector2.Zero;
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
        ResetPosition();

        if (_isBegin)
        {
            _renderer.End();
            _isBegin = false;
            return true;
        }

        return false;
    }
}
