using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.GUI;

namespace Vocore.Engine;

public class UIInputTracker : IUIInputTracker
{
    private readonly InputSystem _input;
    private readonly Window _window;
    public UIInputTracker(InputSystem system, Window window)
    {
        _input = system;
        _window = window;
    }

    public Vector2 WindowSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _window.Size;
    }

    public Vector2 MousePosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _window.GetLocalMousePosition(_input.MousePosition);
    }

    public bool IsMouseUp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMouseUp(Mouse.Left);
    }

    public bool IsMouseDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMouseDown(Mouse.Left);
    }

    public bool IsMousePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMousePressing(Mouse.Left);
    }

    public bool IsMouseScrolling(out Vector2 delta)
    {
        return _input.IsMouseScrolling(out delta);
    }

    public void SetTextInput(int x, int y, int width, int height, int cursor)
    {
        _window.StartTextInput(x, y, width, height, cursor);
    }
}