using System.Numerics;
using Vocore.Rendering;

namespace Vocore.GUI;

public static class ImGui
{
    private static IImGuiRenderer _renderer = new NoImGuiRenderer();
    private static ImGuiStyle _style;
    private static bool _isBegin = false;
    private static Vector2 _currentPosition = Vector2.Zero;
    private static readonly char[] _stringBuffer = new char[1024];
    private static int _stringBufferLength = 0;

    public static void Initialize(IImGuiRenderer renderer, ImGuiStyle style)
    {
        _renderer = renderer;
        _style = style;
    }

    public unsafe static void Text(string text)
    {
        CheckBegin();
        Vector2 tmp = _currentPosition;
        tmp.Y = -tmp.Y;
        fixed (char* ptr = text)
        {
            _renderer.DrawText(tmp, _style.Font, ptr, text.Length, _style.FontSize, _style.TextColor, Pivot.LeftTop);
        }
        _currentPosition.Y += _style.FontSize;
    }

    public unsafe static void Text<T>(T value) where T : ISpanFormattable
    {
        CheckBegin();
        Vector2 tmp = _currentPosition;
        tmp.Y = -tmp.Y;
         value.TryFormat(_stringBuffer, out _stringBufferLength, ReadOnlySpan<char>.Empty, null);
        fixed (char* ptr = _stringBuffer)
        {
            _renderer.DrawText(tmp, _style.Font, ptr, _stringBufferLength, _style.FontSize, _style.TextColor, Pivot.LeftTop);
        }
        _currentPosition.Y += _style.FontSize;
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

        if (_isBegin)
        {
            _renderer.End();
            _isBegin = false;
            return true;
        }

        return false;
    }
}
