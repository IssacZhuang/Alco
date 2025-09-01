using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.GUI;

namespace Alco.Engine;

public class UIInputTracker : IUIInputTracker
{
    private readonly Input _input;
    private readonly View _window;
    
    public UIInputTracker(Input system, View window)
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

    public bool IsMousePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMousePressing(Mouse.Left);
    }

    public bool IsKeyDeletePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Delete);
    }

    public bool IsKeyBackspacePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Backspace);
    }

    public bool IsKeyEnterPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Enter);
    }

    public bool IsKeyTabPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Tab);
    }

    public bool IsKeyLeftPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Left);
    }

    public bool IsKeyRightPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Right);
    }

    public bool IsKeyUpPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Up);
    }

    public bool IsKeyDownPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Down);
    }

    public bool IsKeySelectAllPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.ControlLeft) && _input.IsKeyPressing(KeyCode.A);
    }

    public bool IsKeyCopyPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.ControlLeft) && _input.IsKeyPressing(KeyCode.C);
    }

    public bool IsKeyPastePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.ControlLeft) && _input.IsKeyPressing(KeyCode.V);
    }


    public bool IsMouseScrolling(out Vector2 delta)
    {
        return _input.IsMouseScrolling(out delta);
    }

    public void SetTextInput(float xNorm, float yNorm, float widthNorm, float heightNorm, int cursor)
    {
        uint windowWidth = _window.Size.X;
        uint windowHeight = _window.Size.Y;
        _window.SetTextInputArea((int)(xNorm * windowWidth), (int)(yNorm * windowHeight), (int)(widthNorm * windowWidth), (int)(heightNorm * windowHeight), cursor);
    }

    public void RegisterTextInput(Action<ReadOnlySpan<char>> action)
    {
        _window.OnTextInput += action;
    }

    public void UnregisterTextInput(Action<ReadOnlySpan<char>> action)
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