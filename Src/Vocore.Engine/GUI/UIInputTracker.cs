using System.Numerics;
using System.Runtime.CompilerServices;
using Vocore.GUI;

namespace Vocore.Engine;

public class UIInputTracker : IUIInputTracker
{
    private readonly InputSystem _system;
    public UIInputTracker(InputSystem system)
    {
        _system = system;
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