using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.GUI;

namespace Alco.Engine;

public class UIInputTracker : IUIInputTracker
{
    private readonly InputSystem _input;
    private readonly View _window;

    public event Action<string>? OnTextInput
    {
        add => _window.OnTextInput += value;
        remove => _window.OnTextInput -= value;
    }
    public UIInputTracker(InputSystem system, View window)
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
        get => _window.MousePosition;
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

    public bool IsKeyDeleteDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyDown(KeyCode.Delete);
    }

    public bool IsKeyBackspaceDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyDown(KeyCode.Backspace);
    }

    public bool IsKeyEnterDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyDown(KeyCode.Enter);
    }

    public bool IsKeyTabDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyDown(KeyCode.Tab);
    }

    public bool IsKeyLeftDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyDown(KeyCode.Left);
    }

    public bool IsKeyRightDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyDown(KeyCode.Right);
    }

    public bool IsKeyUp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyDown(KeyCode.Up);
    }

    public bool IsKeyDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyDown(KeyCode.Down);
    }

    public bool IsKeyLeft
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyDown(KeyCode.Left);
    }

    public bool IsKeyRight
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyDown(KeyCode.Right);
    }

    public bool IsKeySelectAllDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.ControlLeft) && _input.IsKeyDown(KeyCode.A);
    }

    public bool IsKeyCopyDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.ControlLeft) && _input.IsKeyDown(KeyCode.C);
    }

    public bool IsKeyPasteDown
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.ControlLeft) && _input.IsKeyDown(KeyCode.V);
    }


    public bool IsMouseScrolling(out Vector2 delta)
    {
        return _input.IsMouseScrolling(out delta);
    }

    public void SetTextInput(float xNorm, float yNorm, float widthNorm, float heightNorm, int cursor)
    {
        uint windowWidth = _window.Size.X;
        uint windowHeight = _window.Size.Y;
        _window.StartTextInput((int)(xNorm * windowWidth), (int)(yNorm * windowHeight), (int)(widthNorm * windowWidth), (int)(heightNorm * windowHeight), cursor);
    }

    public void EndTextInput()
    {
        _window.EndTextInput();
    }

    public void RegisterTextInput(Action<string> action)
    {
        _window.OnTextInput += action;
    }

    public void UnregisterTextInput(Action<string> action)
    {
        _window.OnTextInput -= action;
    }

    public void CopyToClipboard(ReadOnlySpan<char> text)
    {
        _input.CopyToClipboard(text);
    }

    public ReadOnlySpan<char> GetClipboardText()
    {
        return _input.GetClipboardText();
    }
}