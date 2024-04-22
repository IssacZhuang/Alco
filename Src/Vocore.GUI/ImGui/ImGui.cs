using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.Graphics;
using Vocore.Rendering;

namespace Vocore.GUI;

public static partial class ImGui
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
