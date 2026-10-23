using System.Numerics;
using System.Runtime.CompilerServices;
using Alco.GUI;

namespace Alco.Engine;

/// <summary>Adapts engine input to the UI input interface.</summary>
public class UIInputTracker : IUIInputTracker
{
    private readonly Input _input;
    private readonly View _window;

    /// <summary>The gamepad button treated as the confirm/click button, or null to disable gamepad clicks.</summary>
    public GamepadButton? GamepadClickButton { get; set; } = null;

    /// <summary>The minimum gamepad stick magnitude required to scroll.</summary>
    public float ScrollDeadZone { get; set; } = 0.1f;

    /// <summary>
    /// Sensitivity multiplier for mouse scroll wheel input.
    /// </summary>
    public float MouseScrollSensitivity { get; set; } = 1.0f;

    /// <summary>
    /// Sensitivity multiplier for gamepad scroll input.
    /// </summary>
    public float GamepadScrollSensitivity { get; set; } = 0.1f;

    /// <summary>Creates a tracker for the given input system and window.</summary>
    /// <param name="system">The input system to read from.</param>
    /// <param name="window">The window whose coordinates are used.</param>
    public UIInputTracker(Input system, View window)
    {
        _input = system;
        _window = window;
    }

    /// <summary>Gets the window size in pixels.</summary>
    public Vector2 WindowSize
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _window.Size;
    }

    /// <summary>Gets the cursor position in window coordinates.</summary>
    public Vector2 CursorPosition
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _window.MousePosition;
    }

    /// <summary>
    /// Indicates whether the mouse left button is currently being pressed.
    /// </summary>
    public bool IsMouseLeftPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsMousePressing(Mouse.Left);
    }

    /// <summary>
    /// Indicates whether the confirm button (gamepad) is currently being pressed.
    /// </summary>
    public bool IsConfirmPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GamepadClickButton.HasValue && (_input.PrimaryGamepad?.IsButtonPressed(GamepadClickButton.Value) ?? false);
    }

    /// <summary>Gets whether the Delete key is currently pressed.</summary>
    public bool IsKeyDeletePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Delete);
    }

    /// <summary>Gets whether the Backspace key is currently pressed.</summary>
    public bool IsKeyBackspacePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Backspace);
    }

    /// <summary>Gets whether the Enter key is currently pressed.</summary>
    public bool IsKeyEnterPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Enter);
    }

    /// <summary>Gets whether the Tab key is currently pressed.</summary>
    public bool IsKeyTabPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.Tab);
    }

    /// <summary>Gets whether left navigation input is currently pressed.</summary>
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

    /// <summary>Gets whether right navigation input is currently pressed.</summary>
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

    /// <summary>Gets whether up navigation input is currently pressed.</summary>
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

    /// <summary>Gets whether down navigation input is currently pressed.</summary>
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

    /// <summary>Gets whether the select-all shortcut (Ctrl+A) is currently pressed.</summary>
    public bool IsKeySelectAllPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.ControlLeft) && _input.IsKeyPressing(KeyCode.A);
    }

    /// <summary>Gets whether the copy shortcut (Ctrl+C) is currently pressed.</summary>
    public bool IsKeyCopyPressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.ControlLeft) && _input.IsKeyPressing(KeyCode.C);
    }

    /// <summary>Gets whether the paste shortcut (Ctrl+V) is currently pressed.</summary>
    public bool IsKeyPastePressing
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsKeyPressing(KeyCode.ControlLeft) && _input.IsKeyPressing(KeyCode.V);
    }


    /// <summary>Gets the current scroll delta from the mouse wheel or gamepad stick.</summary>
    /// <param name="delta">The scroll delta when scrolling is active.</param>
    /// <returns>True when scrolling is active.</returns>
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

    /// <summary>Sets the normalized area used for text input and the cursor position.</summary>
    /// <param name="xNorm">The normalized left of the input area.</param>
    /// <param name="yNorm">The normalized top of the input area.</param>
    /// <param name="widthNorm">The normalized width of the input area.</param>
    /// <param name="heightNorm">The normalized height of the input area.</param>
    /// <param name="cursor">The cursor position within the text.</param>
    public void SetTextInput(float xNorm, float yNorm, float widthNorm, float heightNorm, int cursor)
    {
        uint windowWidth = _window.Size.X;
        uint windowHeight = _window.Size.Y;
        _window.SetTextInputArea((int)(xNorm * windowWidth), (int)(yNorm * windowHeight), (int)(widthNorm * windowWidth), (int)(heightNorm * windowHeight), cursor);
    }

    /// <summary>Registers a handler for text input events.</summary>
    /// <param name="action">The handler to register.</param>
    public void RegisterTextInput(Action<ReadOnlySpan<char>> action)
    {
        _window.OnTextInput += action;
    }

    /// <summary>Unregisters a text input handler.</summary>
    /// <param name="action">The handler to unregister.</param>
    public void UnregisterTextInput(Action<ReadOnlySpan<char>> action)
    {
        _window.OnTextInput -= action;
    }

    /// <summary>Copies text to the clipboard.</summary>
    /// <param name="text">The text to copy.</param>
    public void CopyToClipboard(ReadOnlySpan<char> text)
    {
        _input.CopyToClipboard(text);
    }

    /// <summary>Gets the current clipboard text.</summary>
    /// <returns>The clipboard text.</returns>
    public ReadOnlySpan<char> GetClipboardText()
    {
        return _input.GetClipboardText();
    }

    /// <summary>Requests text input from the window.</summary>
    public void RequestTextInput()
    {
        _window.RequestTextInput();
    }

    /// <summary>Releases text input from the window.</summary>
    public void ReleaseTextInput()
    {
        _window.ReleaseTextInput();
    }

    /// <summary>Gets whether gamepad input is currently active.</summary>
    public bool IsGamepadInputting
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _input.IsGamepadInputting;
    }
}