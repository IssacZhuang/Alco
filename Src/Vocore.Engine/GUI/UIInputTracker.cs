using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.GUI;

namespace Vocore.Engine;

public class UIInputTracker : IUIInputTracker
{
    private readonly InputSystem _system;
    private readonly Window _window;
    public UIInputTracker(InputSystem system, Window window)
    {
        _system = system;
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
        get => _system.MousePosition;
    }

    public bool IsMouseClicked
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _system.IsMouseUp(Mouse.Left);
    }

    public bool IsMousePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _system.IsMousePressing(Mouse.Left);
    }
}