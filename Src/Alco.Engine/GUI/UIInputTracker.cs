using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.GUI;

namespace Alco.Engine;

public class UIInputTracker : IUIInputTracker
{
    private readonly Input _input;
    private readonly View _window;

    public GamepadButton? GamepadClickButton { get; set; } = null;

    public float ScrollDeadZone { get; set; } = 0.1f;

    /// <summary>
    /// Sensitivity multiplier for mouse scroll wheel input.
    /// </summary>
    public float MouseScrollSensitivity { get; set; } = 1.0f;

    /// <summary>
    /// Sensitivity multiplier for gamepad scroll input.
    /// </summary>
    public float GamepadScrollSensitivity { get; set; } = 0.1f;

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

    public Vector2 CursorPosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _window.MousePosition;
    }

    public bool IsConfirmPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMousePressing(Mouse.Left) || GamepadClickButton.HasValue && (_input.PrimaryGamepad?.IsButtonPressed(GamepadClickButton.Value) ?? false);
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
        get
        {
            if (_input.IsKeyPressing(KeyCode.Left))
                return true;
            Gamepad? gamepad = _input.PrimaryGamepad;
            return gamepad != null && gamepad.IsButtonPressed(GamepadButton.DPadLeft);
        }
    }

    public bool IsKeyRightPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_input.IsKeyPressing(KeyCode.Right))
                return true;
            Gamepad? gamepad = _input.PrimaryGamepad;
            return gamepad != null && gamepad.IsButtonPressed(GamepadButton.DPadRight);
        }
    }

    public bool IsKeyUpPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_input.IsKeyPressing(KeyCode.Up))
                return true;
            Gamepad? gamepad = _input.PrimaryGamepad;
            return gamepad != null && gamepad.IsButtonPressed(GamepadButton.DPadUp);
        }
    }

    public bool IsKeyDownPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (_input.IsKeyPressing(KeyCode.Down))
                return true;
            Gamepad? gamepad = _input.PrimaryGamepad;
            return gamepad != null && gamepad.IsButtonPressed(GamepadButton.DPadDown);
        }
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


    public bool IsScrolling(out Vector2 delta)
    {
        if (_input.IsMouseScrolling(out delta))
        {
            delta *= MouseScrollSensitivity;
            return true;
        }

        Gamepad? gamepad = _input.PrimaryGamepad;
        if (gamepad != null)
        {
            float rx = gamepad.GetAxis(GamepadAxis.RightX);
            float ry = gamepad.GetAxis(GamepadAxis.RightY);
            if (MathF.Abs(rx) >= ScrollDeadZone || MathF.Abs(ry) >= ScrollDeadZone)
            {
                delta = new Vector2(rx, ry) * GamepadScrollSensitivity;
                return true;
            }
        }

        delta = Vector2.Zero;
        return false;
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

    public void RequestTextInput()
    {
        _window.RequestTextInput();
    }

    public void ReleaseTextInput()
    {
        _window.ReleaseTextInput();
    }

    public bool IsGamepadInputting
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsGamepadInputting;
    }
}