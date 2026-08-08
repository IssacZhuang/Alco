using System.Numerics;
using System.Runtime.CompilerServices;

namespace Alco.Engine;


public static partial class DebugStats
{
    private static DebugStatsRenderer? _renderer;
    private static DebugStatsStyle _style;

    private static readonly char[] _stringBuffer = new char[1024];
    private static int _stringBufferLength = 0;

    private static bool _isSameLine = false;
    private static Vector2 _currentPosition = Vector2.Zero;
    private static Vector2 _nextOffset = Vector2.Zero;

    public static void Initialize(DebugStatsRenderer renderer, DebugStatsStyle style)
    {
        _renderer = renderer;
        _style = style;

        ResetPosition();
    }

    public static void SameLine()
    {
        _isSameLine = true;
    }

    private static Vector2 ProcessPosition()
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

    private static void SetNextOffset(Vector2 offset)
    {
        _nextOffset = new Vector2(offset.X, math.max(_nextOffset.Y, offset.Y));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ResetSameLine()
    {
        _isSameLine = false;
    }

    /// <summary>
    /// Resets the layout cursor to the top-left corner. The system calls this at
    /// the start of each frame before any draw calls.
    /// </summary>
    public static void ResetPosition()
    {
        _currentPosition = new Vector2(_style.Margin.X, _style.Margin.Z + _style.FontSize * 0.5f);
        _nextOffset = Vector2.Zero;
    }
}
